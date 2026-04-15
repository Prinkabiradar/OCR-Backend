using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Utils;

namespace OCR_BACKEND.Services
{

    public sealed record PdfPageResult(
        int PageNumber,
        string FileName,
        string Text,
        bool NeedsOcr,
        string? ChunkPath
    );

    public interface IPdfToImageService
    {

        Task<List<PdfPageResult>> ExtractPagesAsync(
            string pdfPath,
            string outputDir,
            CancellationToken ct = default);
    }

    public sealed class PdfToImageService : IPdfToImageService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<PdfToImageService> _logger;

        // Minimum chars to consider a page "has text"
        private const int MinTextLength = 10;

        public PdfToImageService(IConfiguration config, ILogger<PdfToImageService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<List<PdfPageResult>> ExtractPagesAsync(
            string pdfPath,
            string outputDir,
            CancellationToken ct = default)
        {
            int chunkSize = Math.Max(1, _config.GetValue("Pdf:PagesPerChunk", 16));

            var results = new List<PdfPageResult>();
            var baseName = Path.GetFileNameWithoutExtension(pdfPath);

            try
            {
                await Task.Run(() =>
                {
                    using var reader = new PdfReader(pdfPath);
                    using var srcDoc = new PdfDocument(reader);

                    int totalPages = srcDoc.GetNumberOfPages();
                    if (totalPages == 0)
                        throw new InvalidOperationException("PDF contains no pages.");

                    _logger.LogInformation(
                        "Processing PDF: {File} — {Pages} page(s), chunkSize={Chunk}",
                        pdfPath, totalPages, chunkSize);

                    // ── Step 1: classify every page as text vs scanned ────────
                    var pageClassifications = new List<(int pageNum, bool isScanned, string text)>();

                    for (int pageNum = 1; pageNum <= totalPages; pageNum++)
                    {
                        ct.ThrowIfCancellationRequested();

                        var page = srcDoc.GetPage(pageNum);
                        var strategy = new LocationTextExtractionStrategy();
                        var text = PdfTextExtractor.GetTextFromPage(page, strategy)?.Trim()
                                       ?? string.Empty;

                        pageClassifications.Add((pageNum, text.Length < MinTextLength, text));
                    }

                    // ── Step 2: store text pages directly ─────────────────────
                    foreach (var (pageNum, isScanned, text) in pageClassifications)
                    {
                        if (!isScanned)
                        {
                            results.Add(new PdfPageResult(
                                PageNumber: pageNum,
                                FileName: $"{baseName}_p{pageNum}.pdf",
                                Text: text,
                                NeedsOcr: false,
                                ChunkPath: null
                            ));
                        }
                    }

                    // ── Step 3: bundle scanned pages into chunk PDFs ─────────
                    var scannedPages = pageClassifications
                        .Where(p => p.isScanned)
                        .Select(p => p.pageNum)
                        .ToList();

                    if (scannedPages.Count == 0)
                    {
                        _logger.LogInformation(
                            "PDF {File} — all {Count} page(s) are text, no OCR needed",
                            pdfPath, totalPages);
                        return;
                    }

                    _logger.LogInformation(
                        "PDF {File} — {Scanned} scanned page(s), writing chunk PDFs of up to {ChunkSize} pages",
                        pdfPath, scannedPages.Count, chunkSize);

                    foreach (var pageBatch in scannedPages.Chunk(chunkSize))
                    {
                        ct.ThrowIfCancellationRequested();

                        var firstPage = pageBatch[0];
                        var lastPage = pageBatch[^1];
                        var chunkFileName = firstPage == lastPage
                            ? $"{baseName}_p{firstPage}.pdf"
                            : $"{baseName}_p{firstPage}-{lastPage}.pdf";
                        var chunkPath = Path.Combine(outputDir, chunkFileName);

                        using var chunkWriter = new PdfWriter(chunkPath);
                        using var chunkDoc = new PdfDocument(chunkWriter);

                        srcDoc.CopyPagesTo(pageBatch.ToList(), chunkDoc);

                        _logger.LogDebug(
                            "  Chunk sub-PDF written: pages {Start}-{End} → {File}",
                            firstPage, lastPage, chunkFileName);

                        foreach (var pageNum in pageBatch)
                        {
                            results.Add(new PdfPageResult(
                                PageNumber: pageNum,
                                FileName: $"{baseName}_p{pageNum}.pdf",
                                Text: string.Empty,
                                NeedsOcr: true,
                                ChunkPath: chunkPath
                            ));
                        }
                    }

                    // Sort results by page number so the DB rows are in order
                    results.Sort((a, b) => a.PageNumber.CompareTo(b.PageNumber));

                    //_logger.LogInformation(
                    //    "PDF {File} done — {Text} text page(s), {Scanned} scanned in {Chunks} chunk(s)",
                    //    pdfPath,
                    //    results.Count(r => !r.NeedsOcr),
                    //    results.Count(r => r.NeedsOcr),
                    //    chunks.Count);

                }, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("PDF processing cancelled: {File}", pdfPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PDF processing failed: {File}", pdfPath);
                return new List<PdfPageResult>();
            }

            return results;
        }
    }
}
