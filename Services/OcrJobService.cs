using OCR_BACKEND.Modals;
using OCR_BACKEND.Queue;
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
        private readonly ILogger<OcrJobService> _logger;

        // ── Extensions that go through conversion before OCR ────────────────
        private static readonly HashSet<string> _convertible = new(StringComparer.OrdinalIgnoreCase)
        {
            ".tif", ".tiff",
            ".doc", ".docx",
            ".ppt", ".pptx"
        };

        // ── Extensions Gemini accepts natively ──────────────────────────────
        private static readonly HashSet<string> _nativeOcr = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif", ".pdf"
        };

        public OcrJobService(
            OcrJobDBHelper ocrJobDBHelper,
            OcrJobQueue ocrJobQueue,
            IConfiguration config,
            IFileConversionService converter,
            ILogger<OcrJobService> logger)
        {
            _ocrJobDBHelper = ocrJobDBHelper;
            _ocrJobQueue = ocrJobQueue;
            _config = config;
            _converter = converter;
            _logger = logger;
        }

        // ── UploadAndEnqueue ────────────────────────────────────────────────

        public async Task<Guid> UploadAndEnqueue(
            List<IFormFile> files,
            CancellationToken ct = default)
        {
            // 1. Create a temp directory for this job
            var jobId = Guid.NewGuid();
            var jobDir = Path.Combine(
                _config["FileStorage:Root"] ?? "uploads", jobId.ToString());
            Directory.CreateDirectory(jobDir);

            // 2. Save uploaded files to disk
            var uploadedPaths = new List<string>();
            foreach (var file in files)
            {
                var safeName = SanitiseFileName(file.FileName);
                var destPath = Path.Combine(jobDir, safeName);
                await using var fs = File.Create(destPath);
                await file.CopyToAsync(fs, ct);
                uploadedPaths.Add(destPath);
            }

            // 3. Convert unsupported files — collect final OCR-ready paths
            var ocrReadyPaths = new List<string>();
            var convDir = Path.Combine(jobDir, "converted");
            Directory.CreateDirectory(convDir);

            foreach (var path in uploadedPaths)
            {
                var ext = Path.GetExtension(path);

                if (_nativeOcr.Contains(ext))
                {
                    // Already supported — use as-is
                    ocrReadyPaths.Add(path);
                    continue;
                }

                if (_convertible.Contains(ext))
                {
                    _logger.LogInformation("Converting {File} before OCR", path);
                    var result = await _converter.ConvertAsync(path, convDir, ct);

                    if (result.Success)
                    {
                        // For TIFF multi-page: collect ALL _p1, _p2 … outputs
                        if (ext.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
                            ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
                        {
                            var baseName = Path.GetFileNameWithoutExtension(path);
                            var tiffPages = Directory
                                .EnumerateFiles(convDir, $"{baseName}_p*.jpg")
                                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                .ToList();

                            ocrReadyPaths.AddRange(tiffPages.Count > 0
                                ? tiffPages
                                : new[] { result.OutputPath });
                        }
                        else
                        {
                            ocrReadyPaths.Add(result.OutputPath);
                        }
                    }
                    else
                    {
                        // Conversion failed — log and skip; do NOT enqueue broken file
                        _logger.LogWarning(
                            "Skipping {File} — conversion failed: {Error}",
                            path, result.Error);
                    }
                }
                else
                {
                    // Unknown extension — skip
                    _logger.LogWarning("Skipping unsupported file: {File}", path);
                }
            }

            if (ocrReadyPaths.Count == 0)
                throw new InvalidOperationException(
                    "No processable files remain after conversion. " +
                    "Please upload JPEG, PNG, WEBP, GIF, PDF, TIFF, DOC, DOCX, PPT, or PPTX files.");

            // 4. Persist the job — total = actual OCR pages (after TIFF expansion)
            var dbJobId = await _ocrJobDBHelper.InsertOcrJob(null, ocrReadyPaths.Count);

            // 5. Enqueue the converted (OCR-ready) paths
            await _ocrJobQueue.EnqueueAsync(new OcrJobQueueItem(dbJobId, ocrReadyPaths), ct);

            _logger.LogInformation(
                "Job {JobId} enqueued — {Total} OCR-ready files from {Uploaded} uploaded",
                dbJobId, ocrReadyPaths.Count, files.Count);

            return dbJobId;
        }

        // ── Pass-through queries ─────────────────────────────────────────────

        public Task<DataTable> GetOcrJobs(OcrJobFetchRequest model)
            => _ocrJobDBHelper.GetOcrJobs(model);

        public Task<DataTable> GetOcrJobById(Guid jobId)
            => _ocrJobDBHelper.GetOcrJobById(jobId);

        public Task<DataTable> GetOcrJobResults(Guid jobId)
            => _ocrJobDBHelper.GetOcrJobResults(jobId);

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string SanitiseFileName(string fileName)
        {
            var name = Path.GetFileName(fileName); // strip any path traversal
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}