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

        public OcrJobService(OcrJobDBHelper ocrJobDBHelper,
            OcrJobQueue ocrJobQueue, IConfiguration config)
        {
            _ocrJobDBHelper = ocrJobDBHelper;
            _ocrJobQueue = ocrJobQueue;
            _config = config;
        }

        public async Task<Guid> UploadAndEnqueue(List<IFormFile> files,
            CancellationToken ct = default)
        {
            // Save all files to disk before returning from controller
            var jobId = Guid.NewGuid();
            var jobDir = Path.Combine(
                _config["FileStorage:Root"] ?? "uploads", jobId.ToString());

            Directory.CreateDirectory(jobDir);

            var savedPaths = new List<string>();
            foreach (var file in files)
            {
                var safeName = Path.GetFileName(file.FileName);
                var destPath = Path.Combine(jobDir, safeName);
                await using var fs = File.Create(destPath);
                await file.CopyToAsync(fs, ct);
                savedPaths.Add(destPath);
            }

            // Persist job record first, then enqueue
            var dbJobId = await _ocrJobDBHelper.InsertOcrJob(null, files.Count);
            await _ocrJobQueue.EnqueueAsync(new OcrJobQueueItem(dbJobId, savedPaths), ct);

            return dbJobId;
        }

        public async Task<DataTable> GetOcrJobs(OcrJobFetchRequest model)
        {
            return await _ocrJobDBHelper.GetOcrJobs(model);
        }

        public async Task<DataTable> GetOcrJobById(Guid jobId)
        {
            return await _ocrJobDBHelper.GetOcrJobById(jobId);
        }

        public async Task<DataTable> GetOcrJobResults(Guid jobId)
        {
            return await _ocrJobDBHelper.GetOcrJobResults(jobId);
        }
    }
}
