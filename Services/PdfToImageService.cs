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
            // How many scanned pages to bundle per Gemini call (default 3)
            int chunkSize = _config.GetValue("Pdf:PagesPerChunk", 3);

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

                    // ── Step 3: write one sub-PDF per scanned page ────────────────────
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
                        "PDF {File} — {Scanned} scanned page(s), writing one sub-PDF each",
                        pdfPath, scannedPages.Count);

                    foreach (var pageNum in scannedPages)
                    {
                        ct.ThrowIfCancellationRequested();

                        var pageFileName = $"{baseName}_p{pageNum}.pdf";
                        var pagePath = Path.Combine(outputDir, pageFileName);

                        using var pageWriter = new PdfWriter(pagePath);
                        using var pageDoc = new PdfDocument(pageWriter);

                        srcDoc.CopyPagesTo(new List<int> { pageNum }, pageDoc);

                        _logger.LogDebug(
                            "  Page sub-PDF written: page {Page} → {File}",
                            pageNum, pageFileName);

                        results.Add(new PdfPageResult(
                            PageNumber: pageNum,
                            FileName: pageFileName,
                            Text: string.Empty,
                            NeedsOcr: true,
                            ChunkPath: pagePath      // unique path per page now
                        ));
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
