using OCR_BACKEND.Modals;
using OCR_BACKEND.Queue;
using OCR_BACKEND.Services;

namespace OCR_BACKEND.BackgroundServices
{
    public class OcrWorkerService : BackgroundService
    {
        // ── Tuning knobs ─────────────────────────────────────────────────────
        private const int MaxParallelChunks = 5;   // was 3 — Gemini Flash handles this easily
        private const int MaxRetries = 7;
        private const int RetryBaseDelayMs = 5000;
        private const int MaxRetryDelayMs = 60_000;
        private const int StaggerDelayMs = 500;

        private readonly OcrJobQueue _ocrJobQueue;
        private readonly OcrJobDBHelper _ocrJobDBHelper;
        private readonly GeminiService _gemini;
        private readonly ILogger<OcrWorkerService> _logger;
        private readonly IConfiguration _config;

        public OcrWorkerService(
            OcrJobQueue ocrJobQueue,
            OcrJobDBHelper ocrJobDBHelper,
            GeminiService gemini,
            ILogger<OcrWorkerService> logger,
            IConfiguration config)
        {
            _ocrJobQueue = ocrJobQueue;
            _ocrJobDBHelper = ocrJobDBHelper;
            _gemini = gemini;
            _logger = logger;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var item in _ocrJobQueue.ReadAllAsync(stoppingToken))
            {
                // Fire-and-forget — each job runs independently
                _ = ProcessJobAsync(item, stoppingToken);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        private async Task ProcessJobAsync(OcrJobQueueItem item, CancellationToken ct)
        {
            _logger.LogInformation("Job {JobId} started — {Count} chunk(s)",
                item.JobId, item.FilePaths.Count);

            await _ocrJobDBHelper.UpdateJobStatus(item.JobId, "Processing", 0);

            try
            {
                var storageRoot = Path.GetFullPath(_config["FileStorage:Root"] ?? "uploads");
                var semaphore = new SemaphoreSlim(MaxParallelChunks);

                // ── Launch all chunks in parallel, capped by semaphore ────────
                var tasks = new List<Task<OcrJobResult>>();
                foreach (var filePath in item.FilePaths)
                {
                    await Task.Delay(StaggerDelayMs, ct); // stagger each launch by 500ms
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync(ct);
                        try
                        {
                            var originalPath = item.SourceFileMap.TryGetValue(filePath, out var src) ? src : filePath;
                            return await ProcessFileWithRetryAsync(
                                item.JobId, filePath, originalPath,
                                Path.GetFullPath(_config["FileStorage:Root"] ?? "uploads"), ct);
                        }
                        finally { semaphore.Release(); }
                    }, ct));
                }

                var results = await Task.WhenAll(tasks);

                // ── Bulk-insert in batches of 5, update progress after each ──
                int processedCount = 0;
                var batch = new List<OcrJobResult>();

                foreach (var result in results)
                {
                    batch.Add(result);
                    processedCount++;

                    if (batch.Count >= 5 || processedCount == results.Length)
                    {
                        await _ocrJobDBHelper.BulkInsertJobResults(batch);
                        batch.Clear();

                        await _ocrJobDBHelper.UpdateJobStatus(
                            item.JobId, "Processing", processedCount);

                        _logger.LogInformation("Job {JobId} — {Done}/{Total} done",
                            item.JobId, processedCount, results.Length);
                    }
                }

                await _ocrJobDBHelper.UpdateJobStatus(
                    item.JobId, "Completed", results.Length);

                CleanupConvertedDirectory(item);

                _logger.LogInformation("Job {JobId} completed", item.JobId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Job {JobId} cancelled", item.JobId);
                await _ocrJobDBHelper.UpdateJobStatus(item.JobId, "Failed", 0, "Job was cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", item.JobId);
                await _ocrJobDBHelper.UpdateJobStatus(item.JobId, "Failed", 0, ex.Message);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        private async Task<OcrJobResult> ProcessFileWithRetryAsync(
            Guid jobId,
            string filePath,
            string originalSourcePath,
            string storageRoot,
            CancellationToken ct)
        {
            var fileName = Path.GetFileName(filePath);

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(filePath, ct);
                    var contentType = ResolveContentType(filePath);

                    // ✅ Uses the new unified method name
                    var ocrText = await _gemini.ExtractTextFromFileBytes(bytes, contentType);

                    if (IsRetryableResponse(ocrText))
                    {
                        _logger.LogWarning("File {File} got 503 (attempt {A}/{Max})",
                            fileName, attempt, MaxRetries);

                        if (attempt == MaxRetries) break;

                        var baseDelay = RetryBaseDelayMs * (int)Math.Pow(2, attempt - 1);
                        var jitter = Random.Shared.Next(0, 2000); // 0–2s random jitter
                        var delay = Math.Min(baseDelay + jitter, MaxRetryDelayMs);
                        _logger.LogWarning("Retrying {File} in {Delay}ms (attempt {A}/{Max})", fileName, delay, attempt, MaxRetries);
                        await Task.Delay(delay, ct);
                        continue;
                    }

                    return new OcrJobResult
                    {
                        JobId = jobId,
                        FileName = Path.GetFileName(originalSourcePath),
                        OcrText = ocrText,
                        Success = true,
                        FilePath = Path.GetRelativePath(
                            storageRoot,
                            Path.GetFullPath(originalSourcePath))
                    };
                }
                catch (Exception ex) when (attempt < MaxRetries)
                {
                    _logger.LogWarning("File {File} error (attempt {A}): {Msg}",
                        fileName, attempt, ex.Message);

                    var delay = Math.Min(RetryBaseDelayMs * (int)Math.Pow(2, attempt - 1), MaxRetryDelayMs);
                    await Task.Delay(delay, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError("File {File} failed after {Max} attempts: {Msg}",
                        fileName, MaxRetries, ex.Message);

                    return new OcrJobResult
                    {
                        JobId = jobId,
                        FileName = fileName,
                        Success = false,
                        Error = ex.Message
                    };
                }
            }

            return new OcrJobResult
            {
                JobId = jobId,
                FileName = fileName,
                Success = false,
                Error = "Gemini returned 503 after all retries"
            };
        }

        // ────────────────────────────────────────────────────────────────────
        private void CleanupConvertedDirectory(OcrJobQueueItem item)
        {
            try
            {
                var dir = Path.GetDirectoryName(item.FilePaths[0]);
                if (dir is null) return;

                if (Path.GetFileName(dir).Equals("converted", StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                    _logger.LogInformation("Cleaned up converted/ for job {JobId}", item.JobId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Cleanup failed for job {JobId}: {Msg}", item.JobId, ex.Message);
            }
        }

        private static bool IsRetryableResponse(string json)
      => json.Contains("\"code\": 503") || json.Contains("\"code\":503")
      || json.Contains("\"code\": 429") || json.Contains("\"code\":429")
      || json.Contains("overloaded")
      || json.Contains("RESOURCE_EXHAUSTED")
      || json.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

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