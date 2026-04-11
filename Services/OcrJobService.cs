using OCR_BACKEND.Modals;
using OCR_BACKEND.Queue;
using OCR_BACKEND.Services;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OCR_BACKEND.Services
{
    public interface IOcrJobService
    {
        Task<Guid> UploadAndEnqueue(List<IFormFile> files, CancellationToken ct = default);
        Task<DataTable> GetOcrJobs(OcrJobFetchRequest model);
        Task<DataTable> GetOcrJobById(Guid jobId);
        Task<DataTable> GetOcrJobResults(Guid jobId);
        Task<OcrJobResult> RetryResult(Guid jobId, string fileName, CancellationToken ct = default);
        Task CancelJob(Guid jobId, CancellationToken ct = default);
    }

    public class OcrJobService : IOcrJobService
    {
        private readonly OcrJobDBHelper _ocrJobDBHelper;
        private readonly OcrJobQueue _ocrJobQueue;
        private readonly IConfiguration _config;
        private readonly IFileConversionService _converter;
        private readonly IPdfToImageService _pdfProcessor;
        private readonly GeminiService _gemini;
        private readonly OcrJobCancellationRegistry _cancellationRegistry;
        private readonly ILogger<OcrJobService> _logger;

        private static readonly HashSet<string> _nativeImageOcr = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif"
        };

        private static readonly HashSet<string> _convertible = new(StringComparer.OrdinalIgnoreCase)
        {
            ".tif", ".tiff", ".doc", ".docx", ".ppt", ".pptx"
        };

        public OcrJobService(
            OcrJobDBHelper ocrJobDBHelper,
            OcrJobQueue ocrJobQueue,
            IConfiguration config,
            IFileConversionService converter,
            IPdfToImageService pdfProcessor,
            GeminiService gemini,
            OcrJobCancellationRegistry cancellationRegistry,
            ILogger<OcrJobService> logger)
        {
            _ocrJobDBHelper = ocrJobDBHelper;
            _ocrJobQueue = ocrJobQueue;
            _config = config;
            _converter = converter;
            _pdfProcessor = pdfProcessor;
            _gemini = gemini;
            _cancellationRegistry = cancellationRegistry;
            _logger = logger;
        }

        // ────────────────────────────────────────────────────────────────────
        public async Task<Guid> UploadAndEnqueue(
    List<IFormFile> files,
    CancellationToken ct = default)
        {
            // ── 1. Insert job into DB first to get the real job_id ───────────────
            var dbJobId = await _ocrJobDBHelper.InsertOcrJob(null, 0);
            _cancellationRegistry.Register(dbJobId);

            // ── 2. Create folder using the DB job_id so they always match ────────
            var jobDir = Path.Combine(_config["FileStorage:Root"] ?? "uploads", dbJobId.ToString());
            var originalsDir = Path.Combine(jobDir, "originals");
            var convDir = Path.Combine(jobDir, "converted");

            Directory.CreateDirectory(originalsDir);
            Directory.CreateDirectory(convDir);

            // ── 3. Save uploaded files ────────────────────────────────────────────
            var uploadedPaths = new List<string>();
            foreach (var file in files)
            {
                var safeName = SanitiseFileName(file.FileName);
                var destPath = Path.Combine(originalsDir, safeName);
                await using var fs = File.Create(destPath);
                await file.CopyToAsync(fs, ct);
                uploadedPaths.Add(destPath);
            }

            var ocrWorkItems = new List<OcrJobWorkItem>();
            var preExtracted = new List<OcrJobResult>();

            // ── 4. Classify and route every uploaded file ─────────────────────────
            foreach (var path in uploadedPaths)
            {
                var ext = Path.GetExtension(path);

                if (_nativeImageOcr.Contains(ext))
                {
                    ocrWorkItems.Add(new OcrJobWorkItem(
                        path,
                        path,
                        new List<OcrJobPageReference>
                        {
                    new(1, Path.GetFileName(path))
                        }));
                    continue;
                }

                if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    await ProcessPdfAsync(path, convDir, ocrWorkItems, preExtracted, ct);
                    continue;
                }

                if (_convertible.Contains(ext))
                {
                    if (ext.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
                    {
                        var result = await _converter.ConvertAsync(path, convDir, ct);
                        if (!result.Success)
                        {
                            _logger.LogWarning("Skipping {File}: {Error}", path, result.Error);
                            continue;
                        }

                        var baseName = Path.GetFileNameWithoutExtension(path);
                        var tiffPages = Directory
                            .EnumerateFiles(convDir, $"{baseName}_p*.jpg")
                            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        var tiffOutputs = tiffPages.Count > 0
                            ? tiffPages.AsEnumerable()
                            : new[] { result.OutputPath }.AsEnumerable();

                        foreach (var imagePath in tiffOutputs)
                        {
                            ocrWorkItems.Add(new OcrJobWorkItem(
                                imagePath,
                                path,
                                new List<OcrJobPageReference>
                                {
                            new(1, Path.GetFileName(imagePath))
                                }));
                        }
                        continue;
                    }

                    // Office → PDF → iText7
                    var officeResult = await _converter.ConvertAsync(path, convDir, ct);
                    if (!officeResult.Success)
                    {
                        _logger.LogWarning("Skipping {File}: {Error}", path, officeResult.Error);
                        continue;
                    }

                    if (Path.GetExtension(officeResult.OutputPath)
                            .Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                        await ProcessPdfAsync(officeResult.OutputPath, convDir, ocrWorkItems, preExtracted, ct);
                    else
                        ocrWorkItems.Add(new OcrJobWorkItem(
                            officeResult.OutputPath,
                            path,
                            new List<OcrJobPageReference>
                            {
                        new(1, Path.GetFileName(officeResult.OutputPath))
                            }));

                    continue;
                }

                _logger.LogWarning("Skipping unsupported file: {File}", path);
            }

            int totalItems = preExtracted.Count + ocrWorkItems.Sum(GetWorkItemPageCount);
            if (totalItems == 0)
            {
                // Mark the already-inserted job as failed before throwing
                await _ocrJobDBHelper.UpdateJobStatus(
                    dbJobId, "Failed", 0,
                    "No processable files remain. Upload JPEG, PNG, WEBP, GIF, PDF, TIFF, DOC, DOCX, PPT, or PPTX.");

                throw new InvalidOperationException(
                    "No processable files remain. Upload JPEG, PNG, WEBP, GIF, PDF, TIFF, DOC, DOCX, PPT, or PPTX.");
            }

            // ── 5. Update DB record with the real total_files count ───────────────
            await _ocrJobDBHelper.InsertOcrJob(dbJobId, totalItems);

            foreach (var r in preExtracted)
                r.JobId = dbJobId;

            // ── 6. Store text pages immediately (no Gemini needed) ────────────────
            if (preExtracted.Count > 0)
            {
                await _ocrJobDBHelper.BulkInsertJobResults(preExtracted);
                _logger.LogInformation(
                    "Job {JobId} — {Count} text page(s) stored directly from iText7",
                    dbJobId, preExtracted.Count);
            }

            // ── 7. Enqueue image/chunk items for Gemini worker ────────────────────
            if (ocrWorkItems.Count > 0)
            {
                await _ocrJobQueue.EnqueueAsync(
                    new OcrJobQueueItem(dbJobId, ocrWorkItems), ct);

                _logger.LogInformation(
                    "Job {JobId} — {Count} OCR page(s) queued across {ChunkCount} work item(s)",
                    dbJobId,
                    ocrWorkItems.Sum(GetWorkItemPageCount),
                    ocrWorkItems.Count);
            }
            else
            {
                // All pages were text — nothing left to do
                await _ocrJobDBHelper.UpdateJobStatus(dbJobId, "Completed", totalItems);
            }

            return dbJobId;
        }
        // ────────────────────────────────────────────────────────────────────
        // PDF processing: iText7 text extraction + sub-PDFs for scanned pages
        // ────────────────────────────────────────────────────────────────────
        private async Task ProcessPdfAsync(
            string pdfPath,
            string convDir,
            List<OcrJobWorkItem> ocrWorkItems,
            List<OcrJobResult> preExtracted,
            CancellationToken ct)
        {
            var pages = await _pdfProcessor.ExtractPagesAsync(pdfPath, convDir, ct);

            if (pages.Count == 0)
            {
                _logger.LogWarning("No pages extracted from {File}", pdfPath);
                preExtracted.Add(new OcrJobResult
                {
                    JobId = Guid.Empty,
                    FileName = Path.GetFileName(pdfPath),
                    OcrText = "{}",
                    Success = false,
                    Error = "Could not extract any pages from PDF"
                });
                return;
            }

            var scannedCount = pages.Count(p => p.NeedsOcr);

            // ✅ NEW: short-circuit if PDF is entirely text-based — skip Gemini
            if (scannedCount == 0)
            {
                _logger.LogInformation(
                    "PDF {File} is fully text-based ({Count} page(s)) — skipping Gemini entirely",
                    Path.GetFileName(pdfPath), pages.Count);

                preExtracted.AddRange(pages.Select(p => new OcrJobResult
                {
                    JobId = Guid.Empty,
                    FileName = p.FileName,
                    OcrText = WrapAsGeminiJson(p.Text),
                    Success = true,
                    FilePath = GetRelativeOriginalPath(pdfPath)
                }));
                return;
            }

            // Mixed or fully scanned PDF — handle page by page
            foreach (var page in pages)
            {
                if (!page.NeedsOcr)
                {
                    preExtracted.Add(new OcrJobResult
                    {
                        JobId = Guid.Empty,
                        FileName = page.FileName,
                        OcrText = WrapAsGeminiJson(page.Text),
                        Success = true,
                        FilePath = GetRelativeOriginalPath(pdfPath)
                    });
                }
                else
                {
                    continue;
                }
            }

            var chunkWorkItems = pages
                .Where(p => p.NeedsOcr && !string.IsNullOrWhiteSpace(p.ChunkPath))
                .GroupBy(p => p.ChunkPath!, StringComparer.OrdinalIgnoreCase)
                .Select(group => new OcrJobWorkItem(
                    group.Key,
                    pdfPath,
                    group.OrderBy(p => p.PageNumber)
                        .Select(p => new OcrJobPageReference(p.PageNumber, p.FileName))
                        .ToList()))
                .ToList();

            ocrWorkItems.AddRange(chunkWorkItems);
        }

        // ────────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────────
        private string GetRelativeOriginalPath(string absolutePath)
        {
            var root = Path.GetFullPath(_config["FileStorage:Root"] ?? "uploads");
            var full = Path.GetFullPath(absolutePath);
            return Path.GetRelativePath(root, full);
        }

        private static string WrapAsGeminiJson(string extractedText)
        {
            var structured = System.Text.Json.JsonSerializer.Serialize(new
            {
                extracted_text = extractedText,
                suggested_document_type = "",
                suggested_document_name = ""
            });

            return System.Text.Json.JsonSerializer.Serialize(new
            {
                candidates = new[]
                {
                    new { content = new { parts = new[] { new { text = structured } } } }
                }
            });
        }

        private static int GetWorkItemPageCount(OcrJobWorkItem item)
            => item.Pages.Count == 0 ? 1 : item.Pages.Count;

        public Task<DataTable> GetOcrJobs(OcrJobFetchRequest model) => _ocrJobDBHelper.GetOcrJobs(model);
        public Task<DataTable> GetOcrJobById(Guid jobId) => _ocrJobDBHelper.GetOcrJobById(jobId);
        public Task<DataTable> GetOcrJobResults(Guid jobId) => _ocrJobDBHelper.GetOcrJobResults(jobId);

        public async Task<OcrJobResult> RetryResult(Guid jobId, string fileName, CancellationToken ct = default)
        {
            var existing = await _ocrJobDBHelper.GetJobResult(jobId, fileName);
            if (existing is null)
                throw new InvalidOperationException("OCR result not found.");

            if (string.IsNullOrWhiteSpace(existing.FilePath))
                throw new InvalidOperationException("Original file path is missing for this OCR result.");

            var absoluteOriginalPath = Path.Combine(
                Path.GetFullPath(_config["FileStorage:Root"] ?? "uploads"),
                existing.FilePath);

            if (!File.Exists(absoluteOriginalPath))
                throw new FileNotFoundException("Original source file could not be found.", absoluteOriginalPath);

            var retried = await BuildRetriedResultAsync(existing, absoluteOriginalPath, ct);
            await _ocrJobDBHelper.UpdateJobResult(retried);
            return retried;
        }

        public async Task CancelJob(Guid jobId, CancellationToken ct = default)
        {
            _cancellationRegistry.Register(jobId);
            _cancellationRegistry.Cancel(jobId);
            await _ocrJobDBHelper.UpdateJobStatus(jobId, "Failed", 0, "Job cancelled by user.");
        }

        private static string SanitiseFileName(string fileName)
        {
            var name = Path.GetFileName(fileName);
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private async Task<OcrJobResult> BuildRetriedResultAsync(
            OcrJobResult existing,
            string absoluteOriginalPath,
            CancellationToken ct)
        {
            var ext = Path.GetExtension(absoluteOriginalPath);
            byte[] bytes;
            string contentType;

            if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var pageNumber = ExtractPageNumber(existing.FileName);
                if (pageNumber is null)
                    throw new InvalidOperationException("Could not determine the PDF page number for retry.");

                bytes = ExtractSinglePagePdfBytes(absoluteOriginalPath, pageNumber.Value);
                contentType = "application/pdf";
            }
            else if (_nativeImageOcr.Contains(ext))
            {
                bytes = await File.ReadAllBytesAsync(absoluteOriginalPath, ct);
                contentType = ResolveContentType(absoluteOriginalPath);
            }
            else if (ext.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
                     ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase) ||
                     _convertible.Contains(ext))
            {
                bytes = await BuildRetriedBytesFromConvertedFileAsync(
                    absoluteOriginalPath,
                    existing.FileName,
                    ct);

                contentType = Path.GetExtension(existing.FileName)
                    .Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                    ? "application/pdf"
                    : "image/jpeg";
            }
            else
            {
                throw new InvalidOperationException($"Retry is not supported for {ext} files.");
            }

            var rawResponse = await _gemini.ExtractTextFromFileBytes(bytes, contentType);
            existing.OcrText = NormalizeSinglePageResponse(rawResponse);
            existing.Success = true;
            existing.Error = null;
            return existing;
        }

        private async Task<byte[]> BuildRetriedBytesFromConvertedFileAsync(
            string sourcePath,
            string resultFileName,
            CancellationToken ct)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ocr-retry", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var conversion = await _converter.ConvertAsync(sourcePath, tempDir, ct);
                if (!conversion.Success)
                    throw new InvalidOperationException(conversion.Error ?? "Conversion failed during retry.");

                var outputExt = Path.GetExtension(conversion.OutputPath);
                if (outputExt.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    var pageNumber = ExtractPageNumber(resultFileName);
                    if (pageNumber is null)
                        throw new InvalidOperationException("Could not determine the converted PDF page number for retry.");

                    return ExtractSinglePagePdfBytes(conversion.OutputPath, pageNumber.Value);
                }

                var pageNumberFromName = ExtractPageNumber(resultFileName);
                if (pageNumberFromName is not null)
                {
                    var baseName = Path.GetFileNameWithoutExtension(sourcePath);
                    var candidate = Directory.EnumerateFiles(tempDir, $"{baseName}_p{pageNumberFromName}.*")
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();

                    if (candidate is not null)
                        return await File.ReadAllBytesAsync(candidate, ct);
                }

                return await File.ReadAllBytesAsync(conversion.OutputPath, ct);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                }
            }
        }

        private static int? ExtractPageNumber(string fileName)
        {
            var match = Regex.Match(fileName, @"_p(?<page>\d+)(?:\D|$)", RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;

            return int.TryParse(match.Groups["page"].Value, out var pageNumber)
                ? pageNumber
                : null;
        }

        private static byte[] ExtractSinglePagePdfBytes(string pdfPath, int pageNumber)
        {
            using var output = new MemoryStream();
            using var reader = new iText.Kernel.Pdf.PdfReader(pdfPath);
            using var writer = new iText.Kernel.Pdf.PdfWriter(output);
            using var source = new iText.Kernel.Pdf.PdfDocument(reader);
            using var target = new iText.Kernel.Pdf.PdfDocument(writer);

            source.CopyPagesTo(new List<int> { pageNumber }, target);
            target.Close();

            return output.ToArray();
        }

        private static string NormalizeSinglePageResponse(string rawResponse)
        {
            try
            {
                using var responseDocument = JsonDocument.Parse(rawResponse);
                if (!responseDocument.RootElement.TryGetProperty("candidates", out var candidates) ||
                    candidates.ValueKind != JsonValueKind.Array ||
                    candidates.GetArrayLength() == 0)
                    return rawResponse;

                var parts = candidates[0].GetProperty("content").GetProperty("parts");
                var textPart = parts.EnumerateArray()
                    .FirstOrDefault(part => part.TryGetProperty("text", out _));

                if (textPart.ValueKind != JsonValueKind.Object ||
                    !textPart.TryGetProperty("text", out var textElement))
                    return rawResponse;

                var candidateText = StripJsonCodeFences(textElement.GetString() ?? string.Empty);
                using var payloadDocument = JsonDocument.Parse(candidateText);
                if (payloadDocument.RootElement.ValueKind != JsonValueKind.Array ||
                    payloadDocument.RootElement.GetArrayLength() == 0)
                    return rawResponse;

                var payload = payloadDocument.RootElement[0].Clone();
                return JsonSerializer.Serialize(new
                {
                    candidates = new[]
                    {
                        new
                        {
                            content = new
                            {
                                parts = new[]
                                {
                                    new
                                    {
                                        text = payload.GetRawText()
                                    }
                                }
                            }
                        }
                    }
                });
            }
            catch
            {
                return rawResponse;
            }
        }

        private static string StripJsonCodeFences(string value)
        {
            var trimmed = value.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
                return trimmed;

            var lines = trimmed.Split('\n').ToList();
            if (lines.Count > 0 && lines[0].StartsWith("```", StringComparison.Ordinal))
                lines.RemoveAt(0);
            if (lines.Count > 0 && lines[^1].Trim().Equals("```", StringComparison.Ordinal))
                lines.RemoveAt(lines.Count - 1);

            return string.Join('\n', lines).Trim();
        }

        private static string ResolveContentType(string path) =>
            Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
    }
}
