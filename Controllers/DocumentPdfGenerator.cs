using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Data;
using System.Net;
using System.Text.RegularExpressions;

namespace OCR_BACKEND.Controllers
{
    public static class DocumentPdfGenerator
    {
        public static byte[] Generate(DataTable pages, int documentId, string documentName)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var pdf = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));

                    // ── HEADER — simple line on every page ────────────────
                    page.Header().Column(col =>
                    {
                        col.Item()
                            .LineHorizontal(1)
                            .LineColor(Colors.Grey.Lighten1);
                    });

                    // ── CONTENT ────────────────────────────────────────────
                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        // ✅ Document name once at top of first page
                        col.Item().PaddingBottom(16).Column(title =>
                        {
                            title.Item()
                                .Text(documentName)
                                .Bold().FontSize(18);

                            title.Item().PaddingTop(4)
                                .Text($"Generated: {DateTime.Now:dd MMM yyyy HH:mm}")
                                .FontSize(9).FontColor(Colors.Grey.Medium);

                            title.Item().PaddingTop(8)
                                .LineHorizontal(1)
                                .LineColor(Colors.Grey.Lighten1);
                        });

                        // ── Pages loop ─────────────────────────────────────
                        foreach (DataRow row in pages.Rows)
                        {
                            int pageNumber = Convert.ToInt32(row["PageNumber"]);
                            string extractedText = StripHtml(
                                row["ExtractedText"]?.ToString() ?? string.Empty
                            );

                            col.Item()
                                .PaddingBottom(12)
                                .Border(1)
                                .BorderColor(Colors.Grey.Lighten2)
                                .Column(inner =>
                                {
                                    inner.Item()
                                        .Background(Colors.Blue.Lighten3)
                                        .Padding(6)
                                        .Text($"Page {pageNumber}")
                                        .Bold().FontSize(11);

                                    inner.Item()
                                        .Padding(10)
                                        .Text(extractedText)
                                        .FontSize(10)
                                        .LineHeight(1.5f);
                                });
                        }
                    });

                    // ── FOOTER ─────────────────────────────────────────────
                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Page ").FontSize(9).FontColor(Colors.Grey.Medium);
                        text.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Medium);
                        text.Span(" of ").FontSize(9).FontColor(Colors.Grey.Medium);
                        text.TotalPages().FontSize(9).FontColor(Colors.Grey.Medium);
                    });
                });
            });

            return pdf.GeneratePdf();
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            html = Regex.Replace(
                html,
                @"<(p|br|div|h[1-6]|li)[^>]*>",
                "\n",
                RegexOptions.IgnoreCase
            );

            html = Regex.Replace(html, @"<[^>]+>", string.Empty);
            html = WebUtility.HtmlDecode(html);
            html = Regex.Replace(html, @"\n{3,}", "\n\n");

            return html.Trim();
        }
    }
}