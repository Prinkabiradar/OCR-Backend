using OCR_BACKEND.Modals;
using OCR_BACKEND.Queue;
using OCR_BACKEND.Services;
using System.Data;

namespace OCR_BACKEND.Services
{
    public interface IOcrJobService
    {
        Task<Guid> UploadAndEnqueue(List<IFormFile> files, CancellationToken ct = default);
        Task<DataTable> GetOcrJobs(OcrJobFetchRequest model);
        Task<DataTable> GetOcrJobById(Guid jobId);
        Task<DataTable> GetOcrJobResults(Guid jobId);
    }

    public class OcrJobService : IOcrJobService
    {
        private readonly OcrJobDBHelper _ocrJobDBHelper;
        private readonly OcrJobQueue _ocrJobQueue;
        private readonly IConfiguration _config;
        private readonly IFileConversionService _converter;
        private readonly IPdfToImageService _pdfProcessor;
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
            ILogger<OcrJobService> logger)
        {
            _ocrJobDBHelper = ocrJobDBHelper;
            _ocrJobQueue = ocrJobQueue;
            _config = config;
            _converter = converter;
            _pdfProcessor = pdfProcessor;
            _logger = logger;
        }

        // ────────────────────────────────────────────────────────────────────
        public async Task<Guid> UploadAndEnqueue(
            List<IFormFile> files,
            CancellationToken ct = default)
        {
            var jobId = Guid.NewGuid();
            var jobDir = Path.Combine(_config["FileStorage:Root"] ?? "uploads", jobId.ToString());
            var originalsDir = Path.Combine(jobDir, "originals");
            var convDir = Path.Combine(jobDir, "converted");

            Directory.CreateDirectory(originalsDir);
            Directory.CreateDirectory(convDir);

            // ── Save uploaded files ──────────────────────────────────────────
            var uploadedPaths = new List<string>();
            foreach (var file in files)
            {
                var safeName = SanitiseFileName(file.FileName);
                var destPath = Path.Combine(originalsDir, safeName);
                await using var fs = File.Create(destPath);
                await file.CopyToAsync(fs, ct);
                uploadedPaths.Add(destPath);
            }

            var ocrReadyPaths = new List<string>();
            var preExtracted = new List<OcrJobResult>();
            var sourceFileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // ── Classify and route every uploaded file ───────────────────────
            foreach (var path in uploadedPaths)
            {
                var ext = Path.GetExtension(path);

                if (_nativeImageOcr.Contains(ext))
                {
                    ocrReadyPaths.Add(path);
                    sourceFileMap[path] = path;
                    continue;
                }

                if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    await ProcessPdfAsync(path, convDir, ocrReadyPaths, preExtracted, sourceFileMap, ct);
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

                        ocrReadyPaths.AddRange(tiffPages.Count > 0 ? tiffPages : new[] { result.OutputPath });
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
                        await ProcessPdfAsync(officeResult.OutputPath, convDir, ocrReadyPaths, preExtracted, sourceFileMap, ct);
                    else
                        ocrReadyPaths.Add(officeResult.OutputPath);

                    continue;
                }

                _logger.LogWarning("Skipping unsupported file: {File}", path);
            }

            int totalItems = preExtracted.Count + ocrReadyPaths.Count;
            if (totalItems == 0)
                throw new InvalidOperationException(
                    "No processable files remain. Upload JPEG, PNG, WEBP, GIF, PDF, TIFF, DOC, DOCX, PPT, or PPTX.");

            // ── Persist job record ───────────────────────────────────────────
            var dbJobId = await _ocrJobDBHelper.InsertOcrJob(null, totalItems);

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
            if (ocrReadyPaths.Count > 0)
            {
                await _ocrJobQueue.EnqueueAsync(
                    new OcrJobQueueItem(dbJobId, ocrReadyPaths, sourceFileMap), ct);

                _logger.LogInformation(
                    "Job {JobId} — {Count} item(s) queued for Gemini OCR",
                    dbJobId, ocrReadyPaths.Count);
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
            List<string> ocrReadyPaths,
            List<OcrJobResult> preExtracted,
            Dictionary<string, string> sourceFileMap,
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
                    // Each scanned page becomes its own single-page sub-PDF
                    if (!ocrReadyPaths.Contains(page.ChunkPath!))
                        ocrReadyPaths.Add(page.ChunkPath!);

                    sourceFileMap[page.ChunkPath!] = pdfPath;
                }
            }
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

        public Task<DataTable> GetOcrJobs(OcrJobFetchRequest model) => _ocrJobDBHelper.GetOcrJobs(model);
        public Task<DataTable> GetOcrJobById(Guid jobId) => _ocrJobDBHelper.GetOcrJobById(jobId);
        public Task<DataTable> GetOcrJobResults(Guid jobId) => _ocrJobDBHelper.GetOcrJobResults(jobId);

        private static string SanitiseFileName(string fileName)
        {
            var name = Path.GetFileName(fileName);
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}