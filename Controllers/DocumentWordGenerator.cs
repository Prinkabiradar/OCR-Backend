using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Data;
using System.Text;

namespace OCR_BACKEND.Controllers
{
    public static class DocumentWordGenerator
    {
        public static byte[] Generate(DataTable pages, int documentId, string documentName)
        {
            using var ms = new MemoryStream();
            using (var wordDoc = WordprocessingDocument.Create(
                       ms, WordprocessingDocumentType.Document, true))
            {
                var mainPart = wordDoc.AddMainDocumentPart();
                mainPart.Document = new Document(new Body());

                // ── Build full HTML from all pages (same as ExtractedText) ─
                var htmlBuilder = new StringBuilder();
                htmlBuilder.AppendLine("<!DOCTYPE html>");
                htmlBuilder.AppendLine("<html><head><meta charset='utf-8'/></head><body>");

                // Document title block
                htmlBuilder.AppendLine($"<h1>{System.Net.WebUtility.HtmlEncode(documentName)}</h1>");
                htmlBuilder.AppendLine($"<p style='color:gray;font-size:9pt'>Generated: {DateTime.Now:dd MMM yyyy HH:mm}</p>");
                htmlBuilder.AppendLine("<hr/>");

                foreach (DataRow row in pages.Rows)
                {
                    int pageNumber = Convert.ToInt32(row["PageNumber"]);
                    string html = row["ExtractedText"]?.ToString() ?? string.Empty;

                    // Page label
                    htmlBuilder.AppendLine($"<p><strong>Page {pageNumber}</strong></p>");
                    htmlBuilder.AppendLine("<hr/>");

                    // Raw extracted HTML — no modification, exactly as-is
                    htmlBuilder.AppendLine(html);

                    htmlBuilder.AppendLine("<br/>");
                }

                htmlBuilder.AppendLine("</body></html>");

                // ── Inject HTML into Word via AltChunk ─────────────────────
                string altChunkId = "altChunkId1";

                var chunk = mainPart.AddAlternativeFormatImportPart(
                    AlternativeFormatImportPartType.Html, altChunkId);

                using (var chunkStream = chunk.GetStream(FileMode.Create, FileAccess.Write))
                {
                    byte[] htmlBytes = Encoding.UTF8.GetBytes(htmlBuilder.ToString());
                    chunkStream.Write(htmlBytes, 0, htmlBytes.Length);
                }

                // Attach chunk to document body
                var altChunk = new AltChunk { Id = altChunkId };
                mainPart.Document.Body!.AppendChild(altChunk);
                mainPart.Document.Save();
            }

            return ms.ToArray();
        }
    }
}