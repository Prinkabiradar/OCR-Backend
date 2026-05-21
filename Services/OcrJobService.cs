using OCR_BACKEND.Modals;
using OCR_BACKEND.Queue;
using OCR_BACKEND.Services;
using System.Security.Cryptography;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OCR_BACKEND.Services
{
    public interface IOcrJobService
    {
        Task<Guid> UploadAndEnqueue(List<IFormFile> files, string? geminiModel = null, CancellationToken ct = default);
        Task<DataTable> GetOcrJobs(OcrJobFetchRequest model);
        Task<DataTable> GetOcrJobById(Guid jobId);
        Task<DataTable> GetOcrJobResults(Guid jobId);
        Task<OcrJobResult> RetryResult(Guid jobId, string fileName, string? geminiModel = null, CancellationToken ct = default);
        Task CancelJob(Guid jobId, CancellationToken ct = default);
        Task<(bool IsHealthy, string Message)> CheckGeminiHealth(string? modelOverride = null, CancellationToken ct = default);
        Task<OcrPageVerificationResult> VerifyPageIntegrity(Guid jobId, int? expectedTotalPages = null, CancellationToken ct = default);
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
        private readonly IStorageService _storage;
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
            IStorageService storage,
            ILogger<OcrJobService> logger)
        {
            _ocrJobDBHelper = ocrJobDBHelper;
            _ocrJobQueue = ocrJobQueue;
            _config = config;
            _converter = converter;
            _pdfProcessor = pdfProcessor;
            _gemini = gemini;
            _cancellationRegistry = cancellationRegistry;
            _storage = storage;
            _logger = logger;
        }

        // ────────────────────────────────────────────────────────────────────
        public async Task<Guid> UploadAndEnqueue(
            List<IFormFile> files,
            string? geminiModel = null,
            CancellationToken ct = default)
        {
            // ── 1. Insert job into DB first to get the real job_id ───────────────
            var dbJobId = await _ocrJobDBHelper.InsertOcrJob(null, 0);
            _cancellationRegistry.Register(dbJobId);

            // ── 3. Save uploaded files using storage service ────────────────────
            var uploadedPaths = new List<string>();
            foreach (var file in files)
            {
                var safeName = SanitiseFileName(file.FileName);
                var uniqueName = BuildUniqueSafeFileName(safeName);
                
                // Save to storage (Digital Ocean or local)
                var storagePath = await _storage.SaveFileAsync(
                    dbJobId.ToString(),
                    "originals",
                    uniqueName,
                    file.OpenReadStream(),
                    ct);

                uploadedPaths.Add(storagePath);
            }

            var ocrWorkItems = new List<OcrJobWorkItem>();
            var preExtracted = new List<OcrJobResult>();

            // ── 4. Classify and route every uploaded file ─────────────────────────
            foreach (var storagePath in uploadedPaths)
            {
                var fileName = Path.GetFileName(storagePath);
                var ext = Path.GetExtension(storagePath);

                if (_nativeImageOcr.Contains(ext))
                {
                    ocrWorkItems.Add(new OcrJobWorkItem(
                        storagePath,
                        storagePath,
                        new List<OcrJobPageReference>
                        {
                            new(1, fileName)
                        }));
                    continue;
                }

                if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    // Get PDF bytes from storage and split into pages
                    var pdfBytes = await _storage.GetFileAsyncBytes(
                        dbJobId.ToString(),
                        "originals",
                        fileName,
                        ct);

                    if (pdfBytes == null)
                    {
                        _logger.LogWarning("Could not retrieve PDF from storage: {FileName}", fileName);
                        continue;
                    }

                    try
                    {
                        var pagePaths = await SplitPdfIntoPagesAsync(pdfBytes, fileName, dbJobId, ct);

                        // Delete the original whole PDF from storage — pages are now stored individually
                        await _storage.DeleteFileAsync(
                            dbJobId.ToString(),
                            "originals",
                            fileName,
                            ct);

                        // Process each single-page PDF
                        foreach (var pagePath in pagePaths)
                        {
                            var pageFileName = Path.GetFileName(pagePath);

                            // Always send to Gemini OCR regardless of text/scanned
                            ocrWorkItems.Add(new OcrJobWorkItem(
                                pagePath,
                                pagePath,
                                new List<OcrJobPageReference>
                                {
                                    new(1, pageFileName)
                                }));
                        }
                    }
                    catch (Exception ex)
                    {
                        var sniffedExt = TryDetectImageExtensionFromBytes(pdfBytes);
                        if (!string.IsNullOrWhiteSpace(sniffedExt))
                        {
                            var correctedName = $"{Path.GetFileNameWithoutExtension(fileName)}_sniffed{sniffedExt}";
                            await using var fallbackStream = new MemoryStream(pdfBytes);
                            var correctedPath = await _storage.SaveFileAsync(
                                dbJobId.ToString(),
                                "converted",
                                correctedName,
                                fallbackStream,
                                ct);

                            _logger.LogWarning(
                                ex,
                                "Failed to parse PDF {File}. Routed as image using detected type {Ext}.",
                                fileName,
                                sniffedExt);

                            ocrWorkItems.Add(new OcrJobWorkItem(
                                correctedPath,
                                storagePath,
                                new List<OcrJobPageReference>
                                {
                                    new(1, Path.GetFileName(correctedPath))
                                }));
                        }
                        else
                        {
                            _logger.LogWarning(ex, "Skipping invalid PDF {File}.", fileName);
                        }
                    }
                    continue;
                }

                if (_convertible.Contains(ext))
                {
                    if (ext.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
                    {
                        // Get file bytes from storage
                        var tiffBytes = await _storage.GetFileAsyncBytes(
                            dbJobId.ToString(),
                            "originals",
                            fileName,
                            ct);

                        if (tiffBytes == null)
                        {
                            _logger.LogWarning("Could not retrieve TIFF from storage: {FileName}", fileName);
                            continue;
                        }

                        var result = await _converter.ConvertFromBytesAsync(
                            tiffBytes,
                            Path.GetExtension(fileName),
                            ct);

                        if (!result.Success)
                        {
                            _logger.LogWarning("Skipping {File}: {Error}", fileName, result.Error);
                            continue;
                        }

                        var baseName = Path.GetFileNameWithoutExtension(fileName);
                        var tiffPages = await _storage.ListFilesAsync(dbJobId.ToString(), "converted", ct);
                        var matchingPages = tiffPages
                            .Where(f => f.StartsWith(baseName, StringComparison.OrdinalIgnoreCase) &&
                                       Regex.IsMatch(f, @"_p\d+\.jpg$", RegexOptions.IgnoreCase))
                            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        var tiffOutputs = matchingPages.Count > 0
                            ? matchingPages.Select(f => $"converted/{f}").ToList()
                            : new List<string> { result.OutputPath };

                        foreach (var imagePath in tiffOutputs)
                        {
                            var imageFileName = Path.GetFileName(imagePath);
                            ocrWorkItems.Add(new OcrJobWorkItem(
                                imagePath,
                                storagePath,
                                new List<OcrJobPageReference>
                                {
                                    new(1, imageFileName)
                                }));
                        }
                        continue;
                    }

                    // Office → PDF → processing
                    var officeBytes = await _storage.GetFileAsyncBytes(
                        dbJobId.ToString(),
                        "originals",
                        fileName,
                        ct);

                    if (officeBytes == null)
                    {
                        _logger.LogWarning("Could not retrieve Office file from storage: {FileName}", fileName);
                        continue;
                    }

                    var officeResult = await _converter.ConvertFromBytesAsync(
                        officeBytes,
                        Path.GetExtension(fileName),
                        ct);

                    if (!officeResult.Success)
                    {
                        _logger.LogWarning("Skipping {File}: {Error}", fileName, officeResult.Error);
                        continue;
                    }

                    if (Path.GetExtension(officeResult.OutputPath)
                            .Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        var pdfBytes2 = await System.IO.File.ReadAllBytesAsync(officeResult.OutputPath, ct);
                        await ProcessPdfAsyncWithBytes(pdfBytes2, Path.GetFileName(officeResult.OutputPath), dbJobId, ocrWorkItems, preExtracted, ct);
                    }
                    else
                    {
                        ocrWorkItems.Add(new OcrJobWorkItem(
                            officeResult.OutputPath,
                            storagePath,
                            new List<OcrJobPageReference>
                            {
                                new(1, Path.GetFileName(officeResult.OutputPath))
                            }));
                    }

                    continue;
                }

                _logger.LogWarning("Skipping unsupported file: {File}", storagePath);
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

            // ── Store text pages immediately (no Gemini needed) ──────────────
            if (preExtracted.Count > 0)
            {
                await _ocrJobDBHelper.BulkInsertJobResults(preExtracted);
                _logger.LogInformation(
                    "Job {JobId} — {Count} text page(s) stored directly from iText7",
                    dbJobId, preExtracted.Count);
            }

            // ── Enqueue image/chunk items for Gemini worker ──────────────────
            if (ocrWorkItems.Count > 0)
            {
                await _ocrJobQueue.EnqueueAsync(
                    new OcrJobQueueItem(dbJobId, ocrWorkItems, geminiModel), ct);

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
        // PDF processing with storage service
        // ────────────────────────────────────────────────────────────────────
        private async Task ProcessPdfAsyncWithBytes(
            byte[] pdfBytes,
            string fileName,
            Guid jobId,
            List<OcrJobWorkItem> ocrWorkItems,
            List<OcrJobResult> preExtracted,
            CancellationToken ct)
        {
            var pages = await _pdfProcessor.ExtractPagesFromBytesAsync(pdfBytes, fileName, ct);

            if (pages.Count == 0)
            {
                _logger.LogWarning("No pages extracted from {File}", fileName);
                preExtracted.Add(new OcrJobResult
                {
                    JobId = Guid.Empty,
                    FileName = fileName,
                    OcrText = "{}",
                    Success = false,
                    Error = "Could not extract any pages from PDF"
                });
                return;
            }

            var scannedCount = pages.Count(p => p.NeedsOcr);

            // If PDF is entirely text-based — skip Gemini
            if (scannedCount == 0)
            {
                _logger.LogInformation(
                    "PDF {File} is fully text-based ({Count} page(s)) — skipping Gemini entirely",
                    fileName, pages.Count);

                preExtracted.AddRange(pages.Select(p => new OcrJobResult
                {
                    JobId = Guid.Empty,
                    FileName = p.FileName,
                    OcrText = WrapAsGeminiJson(p.Text),
                    Success = true,
                    FilePath = $"originals/{p.FileName}"
                }));
                return;
            }

            // Mixed or fully scanned PDF
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
                        FilePath = $"originals/{page.FileName}"
                    });
                }
            }

            var chunkWorkItems = pages
                .Where(p => p.NeedsOcr && !string.IsNullOrWhiteSpace(p.ChunkPath))
                .GroupBy(p => p.ChunkPath!, StringComparer.OrdinalIgnoreCase)
                .Select(group => new OcrJobWorkItem(
                    group.Key,
                    fileName,
                    group.OrderBy(p => p.PageNumber)
                        .Select(p => new OcrJobPageReference(p.PageNumber, p.FileName))
                        .ToList()))
                .ToList();

            ocrWorkItems.AddRange(chunkWorkItems);
        }

        // Split PDF into individual pages and save to storage
        private async Task<List<string>> SplitPdfIntoPagesAsync(
            byte[] pdfBytes,
            string fileName,
            Guid jobId,
            CancellationToken ct)
        {
            var pagePaths = new List<string>();
            var baseName = Path.GetFileNameWithoutExtension(fileName);

            using var sourceStream = new MemoryStream(pdfBytes);
            using var reader = new iText.Kernel.Pdf.PdfReader(sourceStream);
            using var srcDoc = new iText.Kernel.Pdf.PdfDocument(reader);

            int total = srcDoc.GetNumberOfPages();
            for (int pageNum = 1; pageNum <= total; pageNum++)
            {
                var pageFileName = $"{baseName}_p{pageNum}.pdf";
                var output = new MemoryStream();
                
                using (var writer = new iText.Kernel.Pdf.PdfWriter(output))
                using (var target = new iText.Kernel.Pdf.PdfDocument(writer))
                {
                    // Keep the MemoryStream open after iText disposes writer/target.
                    writer.SetCloseStream(false);
                    srcDoc.CopyPagesTo(new List<int> { pageNum }, target);
                }

                output.Position = 0;
                
                // Save to storage
                await _storage.SaveFileAsync(
                    jobId.ToString(),
                    "originals",
                    pageFileName,
                    output,
                    ct);

                pagePaths.Add($"originals/{pageFileName}");
            }

            return pagePaths;
        }

        // ────────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────────
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

        private static string? TryDetectImageExtensionFromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4)
                return null;

            // JPEG
            if (bytes.Length >= 3 &&
                bytes[0] == 0xFF &&
                bytes[1] == 0xD8 &&
                bytes[2] == 0xFF)
                return ".jpg";

            // PNG
            if (bytes.Length >= 8 &&
                bytes[0] == 0x89 &&
                bytes[1] == 0x50 &&
                bytes[2] == 0x4E &&
                bytes[3] == 0x47 &&
                bytes[4] == 0x0D &&
                bytes[5] == 0x0A &&
                bytes[6] == 0x1A &&
                bytes[7] == 0x0A)
                return ".png";

            // GIF87a / GIF89a
            if (bytes.Length >= 6 &&
                bytes[0] == 0x47 &&
                bytes[1] == 0x49 &&
                bytes[2] == 0x46 &&
                bytes[3] == 0x38 &&
                (bytes[4] == 0x37 || bytes[4] == 0x39) &&
                bytes[5] == 0x61)
                return ".gif";

            // WebP (RIFF....WEBP)
            if (bytes.Length >= 12 &&
                bytes[0] == 0x52 &&
                bytes[1] == 0x49 &&
                bytes[2] == 0x46 &&
                bytes[3] == 0x46 &&
                bytes[8] == 0x57 &&
                bytes[9] == 0x45 &&
                bytes[10] == 0x42 &&
                bytes[11] == 0x50)
                return ".webp";

            return null;
        }

        public Task<DataTable> GetOcrJobs(OcrJobFetchRequest model) => _ocrJobDBHelper.GetOcrJobs(model);
        public Task<DataTable> GetOcrJobById(Guid jobId) => _ocrJobDBHelper.GetOcrJobById(jobId);
        public Task<DataTable> GetOcrJobResults(Guid jobId) => _ocrJobDBHelper.GetOcrJobResults(jobId);

        public async Task<OcrJobResult> RetryResult(Guid jobId, string fileName, string? geminiModel = null, CancellationToken ct = default)
        {
            var existing = await _ocrJobDBHelper.GetJobResult(jobId, fileName);
            if (existing is null)
                throw new InvalidOperationException("OCR result not found.");

            if (string.IsNullOrWhiteSpace(existing.FilePath))
                throw new InvalidOperationException("Original file path is missing for this OCR result.");

            // Extract file type and file name from stored path (e.g., "originals/file.pdf" or "converted/file.jpg")
            var pathParts = existing.FilePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (pathParts.Length < 2)
                throw new InvalidOperationException("Invalid file path format in database.");

            var fileType = pathParts[0];  // "originals" or "converted"
            var actualFileName = pathParts[^1];

            // Get file from storage
            var fileBytes = await _storage.GetFileAsyncBytes(jobId.ToString(), fileType, actualFileName, ct);
            if (fileBytes == null || fileBytes.Length == 0)
                throw new FileNotFoundException("Original source file could not be found in storage.", existing.FilePath);

            var retried = await BuildRetriedResultAsync(existing, fileBytes, actualFileName, geminiModel, ct);
            await _ocrJobDBHelper.UpdateJobResult(retried);
            return retried;
        }

        public async Task CancelJob(Guid jobId, CancellationToken ct = default)
        {
            _cancellationRegistry.Register(jobId);
            _cancellationRegistry.Cancel(jobId);
            await _ocrJobDBHelper.UpdateJobStatus(jobId, "Failed", 0, "Job cancelled by user.");
        }

        public async Task<OcrPageVerificationResult> VerifyPageIntegrity(Guid jobId, int? expectedTotalPages = null, CancellationToken ct = default)
        {
            var jobTable = await _ocrJobDBHelper.GetOcrJobById(jobId);
            if (jobTable.Rows.Count == 0)
                throw new InvalidOperationException("Job not found.");

            var results = await _ocrJobDBHelper.GetOcrJobResults(jobId);
            var expected = expectedTotalPages ?? GetExpectedTotalPages(jobTable.Rows[0]);

            var pageOrder = new List<int>();
            var numberedPages = new List<(int PageNumber, string FileName)>();
            var contentHashToFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (DataRow row in results.Rows)
            {
                var fileName = row["file_name"]?.ToString() ?? string.Empty;
                var page = ExtractPageNumber(fileName);
                if (page is not null)
                {
                    pageOrder.Add(page.Value);
                    numberedPages.Add((page.Value, fileName));
                }

                var extractedText = TryExtractGeminiExtractedText(row["ocr_text"]?.ToString());
                if (string.IsNullOrWhiteSpace(extractedText))
                    continue;

                var normalized = NormalizeForDuplicateCompare(extractedText);
                if (string.IsNullOrWhiteSpace(normalized))
                    continue;

                var hash = ComputeSha256(normalized);
                if (!contentHashToFiles.TryGetValue(hash, out var files))
                {
                    files = new List<string>();
                    contentHashToFiles[hash] = files;
                }
                files.Add(fileName);
            }

            var groupedByPage = numberedPages
                .GroupBy(x => x.PageNumber)
                .ToDictionary(g => g.Key, g => g.Select(v => v.FileName).ToList());

            var duplicatePages = groupedByPage
                .Where(kvp => kvp.Value.Count > 1)
                .Select(kvp => kvp.Key)
                .OrderBy(p => p)
                .ToList();

            var canVerifyByPageNumbers = numberedPages.Count > 0;
            var missingPages = expected > 0 && canVerifyByPageNumbers
                ? Enumerable.Range(1, expected).Where(p => !groupedByPage.ContainsKey(p)).ToList()
                : new List<int>();

            var duplicateContentGroups = contentHashToFiles.Values
                .Where(files => files.Count > 1)
                .Select(files => files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList())
                .ToList();

            var result = new OcrPageVerificationResult
            {
                JobId = jobId,
                ExpectedTotalPages = expected,
                ProcessedResultCount = results.Rows.Count,
                DetectedNumberedPageCount = numberedPages.Count,
                IsPageOrderValid = IsStrictlyIncreasing(pageOrder),
                HasMissingPages = missingPages.Count > 0,
                HasDuplicatePages = duplicatePages.Count > 0,
                HasDuplicateContent = duplicateContentGroups.Count > 0,
                DetectedPageOrder = pageOrder,
                MissingPages = missingPages,
                DuplicatePages = duplicatePages
            };

            if (!result.IsPageOrderValid)
            {
                result.Issues.Add(new OcrPageVerificationIssue
                {
                    Type = "page_order",
                    Message = "Detected page sequence is not in ascending order.",
                    PageNumbers = pageOrder
                });
            }

            if (result.HasMissingPages)
            {
                result.Issues.Add(new OcrPageVerificationIssue
                {
                    Type = "missing_pages",
                    Message = "Some page numbers are missing.",
                    PageNumbers = missingPages
                });
            }

            if (result.HasDuplicatePages)
            {
                foreach (var duplicatePage in duplicatePages)
                {
                    result.Issues.Add(new OcrPageVerificationIssue
                    {
                        Type = "duplicate_pages",
                        Message = $"Page {duplicatePage} appears more than once.",
                        PageNumbers = new List<int> { duplicatePage },
                        Files = groupedByPage[duplicatePage]
                    });
                }
            }

            if (result.HasDuplicateContent)
            {
                foreach (var files in duplicateContentGroups)
                {
                    result.Issues.Add(new OcrPageVerificationIssue
                    {
                        Type = "duplicate_content",
                        Message = "Multiple files have identical extracted OCR content.",
                        Files = files
                    });
                }
            }

            if (numberedPages.Count == 0)
            {
                result.Issues.Add(new OcrPageVerificationIssue
                {
                    Type = "unverifiable_page_numbers",
                    Message = "No numbered pages were detected in result file names (expected *_p1, *_p2, ...)."
                });
            }

            result.CanFinalize = result.IsPageOrderValid &&
                                 !result.HasMissingPages &&
                                 !result.HasDuplicatePages &&
                                 (!canVerifyByPageNumbers || numberedPages.Count > 0);

            return result;
        }

        private static string SanitiseFileName(string fileName)
        {
            var name = Path.GetFileName(fileName);
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static string BuildUniqueSafeFileName(string safeFileName)
        {
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(safeFileName);
            var extension = Path.GetExtension(safeFileName);
            return $"{fileNameWithoutExt}_{Guid.NewGuid():N}{extension}";
        }

        private async Task<OcrJobResult> BuildRetriedResultAsync(
            OcrJobResult existing,
            byte[] sourceBytes,
            string sourceFileName,
            string? geminiModel,
            CancellationToken ct)
        {
            var ext = Path.GetExtension(sourceFileName);
            byte[] bytes;
            string contentType;

            if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var pageNumber = ExtractPageNumber(existing.FileName);
                if (pageNumber is null)
                    throw new InvalidOperationException("Could not determine the PDF page number for retry.");

                bytes = ExtractSinglePagePdfBytes(sourceBytes, pageNumber.Value);
                contentType = "application/pdf";
            }
            else if (_nativeImageOcr.Contains(ext))
            {
                bytes = sourceBytes;
                contentType = ResolveContentType(sourceFileName);
            }
            else
            {
                throw new InvalidOperationException($"Retry is not supported for {ext} files.");
            }

            var rawResponse = await _gemini.ExtractTextFromFileBytes(bytes, contentType, geminiModel);
            existing.OcrText = NormalizeSinglePageResponse(rawResponse);
            existing.Success = true;
            existing.Error = null;
            return existing;
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

            var totalPages = source.GetNumberOfPages();
            if (totalPages <= 0)
                throw new InvalidOperationException("The source PDF has no pages.");

            if (pageNumber < 1 || pageNumber > totalPages)
            {
                // Retry often points to _p74 style names, but stored file can be already a single-page split PDF.
                if (totalPages == 1)
                    pageNumber = 1;
                else
                    throw new InvalidOperationException(
                        $"Requested page number {pageNumber} is out of bounds for a PDF with {totalPages} page(s).");
            }

            source.CopyPagesTo(new List<int> { pageNumber }, target);
            target.Close();

            return output.ToArray();
        }

        private static byte[] ExtractSinglePagePdfBytes(byte[] pdfBytes, int pageNumber)
        {
            using var output = new MemoryStream();
            using var input = new MemoryStream(pdfBytes);
            using var reader = new iText.Kernel.Pdf.PdfReader(input);
            using var writer = new iText.Kernel.Pdf.PdfWriter(output);
            using var source = new iText.Kernel.Pdf.PdfDocument(reader);
            using var target = new iText.Kernel.Pdf.PdfDocument(writer);

            var totalPages = source.GetNumberOfPages();
            if (totalPages <= 0)
                throw new InvalidOperationException("The source PDF has no pages.");

            if (pageNumber < 1 || pageNumber > totalPages)
            {
                if (totalPages == 1)
                    pageNumber = 1;
                else
                    throw new InvalidOperationException(
                        $"Requested page number {pageNumber} is out of bounds for a PDF with {totalPages} page(s).");
            }

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
                JsonElement payload;
                if (payloadDocument.RootElement.ValueKind == JsonValueKind.Array &&
                    payloadDocument.RootElement.GetArrayLength() > 0)
                {
                    payload = payloadDocument.RootElement[0].Clone();
                }
                else if (payloadDocument.RootElement.ValueKind == JsonValueKind.Object)
                {
                    payload = payloadDocument.RootElement.Clone();
                }
                else
                {
                    return rawResponse;
                }

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
                                        text = SanitizePayloadJson(payload)
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

        private static string SanitizePayloadJson(JsonElement payload)
        {
            if (payload.ValueKind != JsonValueKind.Object)
                return payload.GetRawText();

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in payload.EnumerateObject())
                {
                    if (property.NameEquals("extracted_text") && property.Value.ValueKind == JsonValueKind.String)
                    {
                        writer.WriteString(property.Name, CleanExtractedText(property.Value.GetString() ?? string.Empty));
                        continue;
                    }

                    property.WriteTo(writer);
                }
                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string CleanExtractedText(string value)
        {
            var cleaned = StripJsonCodeFences(value)
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\r", "\r", StringComparison.Ordinal);

            cleaned = TryUnwrapExtractedTextJson(cleaned);
            return ExtractedTextSanitizer.ToPlainBlackFriendlyText(cleaned);
        }

        private static string TryUnwrapExtractedTextJson(string value)
        {
            try
            {
                var candidate = StripJsonCodeFences(value).Trim();
                using var json = JsonDocument.Parse(candidate);
                return ExtractTextFromJsonElement(json.RootElement)?.Trim() ?? value;
            }
            catch
            {
                return value;
            }
        }

        private static string? ExtractTextFromJsonElement(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    var extracted = ExtractTextFromJsonElement(item);
                    if (!string.IsNullOrWhiteSpace(extracted))
                        return extracted;
                }

                return null;
            }

            if (element.ValueKind != JsonValueKind.Object)
                return null;

            if (element.TryGetProperty("extracted_text", out var extractedText) &&
                extractedText.ValueKind == JsonValueKind.String)
            {
                return extractedText.GetString();
            }

            if (element.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String)
            {
                return text.GetString();
            }

            return null;
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

        // ── Check Gemini API health before processing large batches ────────
        public async Task<(bool IsHealthy, string Message)> CheckGeminiHealth(string? modelOverride = null, CancellationToken ct = default)
        {
            return await _gemini.CheckGeminiHealth(modelOverride, ct);
        }

        private static int GetExpectedTotalPages(DataRow jobRow)
        {
            foreach (DataColumn column in jobRow.Table.Columns)
            {
                if (!string.Equals(column.ColumnName, "total_files", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (jobRow[column] == DBNull.Value)
                    return 0;

                return Convert.ToInt32(jobRow[column]);
            }

            return 0;
        }

        private static bool IsStrictlyIncreasing(List<int> values)
        {
            if (values.Count <= 1)
                return true;

            for (var i = 1; i < values.Count; i++)
            {
                if (values[i] <= values[i - 1])
                    return false;
            }

            return true;
        }

        private static string? TryExtractGeminiExtractedText(string? rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
                return null;

            try
            {
                using var responseDoc = JsonDocument.Parse(rawResponse);
                if (!responseDoc.RootElement.TryGetProperty("candidates", out var candidates) ||
                    candidates.ValueKind != JsonValueKind.Array ||
                    candidates.GetArrayLength() == 0)
                    return null;

                var parts = candidates[0].GetProperty("content").GetProperty("parts");
                var text = parts.EnumerateArray()
                    .FirstOrDefault(part => part.TryGetProperty("text", out _))
                    .GetProperty("text")
                    .GetString();

                if (string.IsNullOrWhiteSpace(text))
                    return null;

                using var payloadDoc = JsonDocument.Parse(StripJsonCodeFences(text));
                if (payloadDoc.RootElement.ValueKind == JsonValueKind.Array &&
                    payloadDoc.RootElement.GetArrayLength() > 0)
                {
                    var first = payloadDoc.RootElement[0];
                    if (first.TryGetProperty("extracted_text", out var extracted) &&
                        extracted.ValueKind == JsonValueKind.String)
                        return extracted.GetString();
                }

                if (payloadDoc.RootElement.ValueKind == JsonValueKind.Object &&
                    payloadDoc.RootElement.TryGetProperty("extracted_text", out var objectExtracted) &&
                    objectExtracted.ValueKind == JsonValueKind.String)
                    return objectExtracted.GetString();
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static string NormalizeForDuplicateCompare(string value)
        {
            var collapsed = Regex.Replace(value, @"\s+", " ").Trim();
            return collapsed.ToLowerInvariant();
        }

        private static string ComputeSha256(string value)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }
    }
}
