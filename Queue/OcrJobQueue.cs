using OCR_BACKEND.Modals;
using System.Threading.Channels;

namespace OCR_BACKEND.Queue
{
    public class OcrJobQueue
    {
        private readonly Channel<OcrJobQueueItem> _channel =
            Channel.CreateBounded<OcrJobQueueItem>(new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

        public ValueTask EnqueueAsync(OcrJobQueueItem item, CancellationToken ct = default)
            => _channel.Writer.WriteAsync(item, ct);

        public IAsyncEnumerable<OcrJobQueueItem> ReadAllAsync(CancellationToken ct = default)
            => _channel.Reader.ReadAllAsync(ct);
    }
}