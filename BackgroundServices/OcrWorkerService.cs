using OCR_BACKEND.Modals;
using OCR_BACKEND.Queue;
using OCR_BACKEND.Services;

namespace OCR_BACKEND.BackgroundServices
{
    public class OcrWorkerService : BackgroundService
    {
        private const int BatchSize = 10;

        private readonly OcrJobQueue _ocrJobQueue;
        private readonly OcrJobDBHelper _ocrJobDBHelper;
        private readonly GeminiService _gemini;
        private readonly ILogger<OcrWorkerService> _logger;

        public OcrWorkerService(OcrJobQueue ocrJobQueue,
            OcrJobDBHelper ocrJobDBHelper,
            GeminiService gemini,
            ILogger<OcrWorkerService> logger)
        {
            _ocrJobQueue = ocrJobQueue;
            _ocrJobDBHelper = ocrJobDBHelper;
            _gemini = gemini;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var item in _ocrJobQueue.ReadAllAsync(stoppingToken))
            {
                // Fire-and-forget — allows multiple jobs to process in parallel
                _ = ProcessJobAsync(item, stoppingToken);
            }
        }

        private async Task ProcessJobAsync(OcrJobQueueItem item, CancellationToken ct)
        {
            _logger.LogInformation("Job {JobId} started — {Count} files",
                item.JobId, item.FilePaths.Count);

            await _ocrJobDBHelper.UpdateJobStatus(item.JobId, "Processing", 0);

            try
            {
                int processedCount = 0;

                foreach (var batch in item.FilePaths.Chunk(BatchSize))
                {
                    var batchTasks = batch.Select(async filePath =>
                    {
                        try
                        {
                            var bytes = await File.ReadAllBytesAsync(filePath, ct);
                            var contentType = ResolveContentType(filePath);
                            var ocrText = await _gemini.ExtractTextFromImageBytes(bytes, contentType);

                            return new OcrJobResult
                            {
                                JobId = item.JobId,
                                FileName = Path.GetFileName(filePath),
                                OcrText = ocrText,
                                Success = true
                            };
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("File {File} in job {JobId} failed: {Msg}",
                                filePath, item.JobId, ex.Message);

                            return new OcrJobResult
                            {
                                JobId = item.JobId,
                                FileName = Path.GetFileName(filePath),
                                Success = false,
                                Error = ex.Message
                            };
                        }
                    });

                    var batchResults = await Task.WhenAll(batchTasks);

                    // Bulk insert batch results into DB
                    await _ocrJobDBHelper.BulkInsertJobResults(batchResults.ToList());

                    processedCount += batchResults.Length;
                    await _ocrJobDBHelper.UpdateJobStatus(item.JobId, "Processing", processedCount);

                    _logger.LogInformation("Job {JobId} progress — {Done}/{Total}",
                        item.JobId, processedCount, item.FilePaths.Count);
                }

                await _ocrJobDBHelper.UpdateJobStatus(item.JobId, "Completed", item.FilePaths.Count);

                // Clean up temp files
                var dir = Path.GetDirectoryName(item.FilePaths[0]);
                if (dir is not null && Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);

                _logger.LogInformation("Job {JobId} completed", item.JobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", item.JobId);
                await _ocrJobDBHelper.UpdateJobStatus(item.JobId, "Failed", 0, ex.Message);
            }
        }

        private static string ResolveContentType(string path) =>
            Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                _ => "application/octet-stream"
            };
    }
}