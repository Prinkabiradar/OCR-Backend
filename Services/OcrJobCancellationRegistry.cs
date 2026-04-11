using System.Collections.Concurrent;

namespace OCR_BACKEND.Services
{
    public class OcrJobCancellationRegistry
    {
        private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _jobs = new();

        public void Register(Guid jobId)
        {
            _jobs.AddOrUpdate(
                jobId,
                _ => new CancellationTokenSource(),
                (_, existing) =>
                {
                    if (existing.IsCancellationRequested)
                    {
                        existing.Dispose();
                        return new CancellationTokenSource();
                    }

                    return existing;
                });
        }

        public CancellationToken GetToken(Guid jobId)
        {
            Register(jobId);
            return _jobs[jobId].Token;
        }

        public bool Cancel(Guid jobId)
        {
            if (!_jobs.TryGetValue(jobId, out var cts))
                return false;

            if (!cts.IsCancellationRequested)
                cts.Cancel();

            return true;
        }

        public void Release(Guid jobId)
        {
            if (_jobs.TryRemove(jobId, out var cts))
                cts.Dispose();
        }
    }
}
