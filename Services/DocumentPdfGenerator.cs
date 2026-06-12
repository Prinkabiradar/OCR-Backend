using HtmlAgilityPack;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using System.Data;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OCR_BACKEND.Services
{
    public static class DocumentPdfGenerator
    {
        public static byte[] Generate(DataTable pages, int documentId)
        {
            using var ms = new MemoryStream();
            using var writer = new PdfWriter(ms);
            using var pdf = new PdfDocument(writer);
            using var doc = new Document(pdf);

            PdfFont bold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            PdfFont normal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            // ✅ Title
            doc.Add(new Paragraph($"Document ID: {documentId}")
                .SetFont(bold)
                .SetFontSize(16)
                .SetMarginBottom(5));

            doc.Add(new Paragraph($"Generated: {DateTime.Now:dd MMM yyyy HH:mm}")
                .SetFont(normal)
                .SetFontSize(12)
                .SetMarginBottom(15));

            foreach (DataRow row in pages.Rows)
            {
                // ✅ Page Header
                doc.Add(new Paragraph($"Page Number: {row["PageNumber"]}")
                    .SetFont(bold)
                    .SetFontSize(13)
                    .SetMarginBottom(5));

                // ✅ Table with proper width
                var table = new Table(new float[] { 150, 350 });
                table.SetWidth(UnitValue.CreatePercentValue(100));


                doc.Add(table);

                // ✅ Extracted Text
                doc.Add(new Paragraph("Extracted Text:")
                    .SetFont(bold)
                    .SetFontSize(10)
                    .SetMarginTop(10));

                doc.Add(new Paragraph(ExtractPlainText(row["ExtractedText"]?.ToString()) ?? "(none)")
                    .SetFont(normal)
                    .SetFontSize(9)
                    .SetMarginBottom(20));

                // ✅ Page Break (IMPORTANT)
                doc.Add(new AreaBreak());
            }

            doc.Close();
            return ms.ToArray();
        }

        private static void AddRow(Table table, string key, object value, PdfFont bold, PdfFont normal)
        {
            table.AddCell(new Cell()
                .Add(new Paragraph(key).SetFont(bold).SetFontSize(9)));

            table.AddCell(new Cell()
                .Add(new Paragraph(value?.ToString() ?? "-").SetFont(normal).SetFontSize(9)));
        }

        private static string ExtractPlainText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var html = DecodeEscapedHtmlMarkup(value);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var text = new StringBuilder();
            AppendText(doc.DocumentNode, text);

            return Regex.Replace(WebUtility.HtmlDecode(text.ToString()), @"[ \t]{2,}", " ").Trim();
        }

        private static void AppendText(HtmlNode node, StringBuilder text)
        {
            if (node.NodeType == HtmlNodeType.Text)
            {
                text.Append(node.InnerText);
                return;
            }

            var tag = node.Name.ToLowerInvariant();
            if (tag == "br")
            {
                text.AppendLine();
                return;
            }

            foreach (var child in node.ChildNodes)
                AppendText(child, text);

            if (tag is "p" or "div" or "li" or "tr" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
                text.AppendLine();
        }

        private static string DecodeEscapedHtmlMarkup(string html)
        {
            var decoded = html;

            for (var i = 0; i < 5; i++)
            {
                if (!Regex.IsMatch(decoded, @"&lt;\s*/?\s*[a-z][\w:-]*(?:\s|/|&gt;)", RegexOptions.IgnoreCase))
                    break;

                var next = WebUtility.HtmlDecode(decoded);
                if (string.Equals(next, decoded, StringComparison.Ordinal))
                    break;

                decoded = next;
            }

            return decoded;
        }
    }
}
