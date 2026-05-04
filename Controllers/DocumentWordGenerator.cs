using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Data;
using System.Linq;
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

                // ── Set internal Word document metadata (title bar + properties) ──
                wordDoc.PackageProperties.Title = documentName;
                wordDoc.PackageProperties.Subject = documentName;
                wordDoc.PackageProperties.Creator = "OCR Backend";
                wordDoc.PackageProperties.Created = DateTime.UtcNow;

                var orderedRows = pages.AsEnumerable()
                    .OrderBy(r => Convert.ToInt32(r["PageNumber"]))
                    .ToList();

                for (int i = 0; i < orderedRows.Count; i++)
                {
                    var row = orderedRows[i];
                    int pageNumber = Convert.ToInt32(row["PageNumber"]);
                    string html = row["ExtractedText"]?.ToString() ?? string.Empty;

                    var htmlBuilder = new StringBuilder();
                    htmlBuilder.AppendLine("<!DOCTYPE html>");
                    htmlBuilder.AppendLine("<html><head><meta charset='utf-8'/></head><body>");

                    // First page: include document title
                    if (i == 0)
                    {
                        htmlBuilder.AppendLine($"<h1>{System.Net.WebUtility.HtmlEncode(documentName)}</h1>");
                        htmlBuilder.AppendLine($"<p style='color:gray;font-size:9pt'>Generated: {DateTime.Now:dd MMM yyyy HH:mm}</p>");
                        htmlBuilder.AppendLine("<hr/>");
                    }

                    // Page label
                    htmlBuilder.AppendLine($"<p><strong>Page {pageNumber}</strong></p>");
                    htmlBuilder.AppendLine("<hr/>");

                    // Raw extracted HTML
                    htmlBuilder.AppendLine(html);
                    htmlBuilder.AppendLine("</body></html>");

                    // Inject this page's HTML as its own AltChunk
                    string altChunkId = $"altChunkId{i + 1}";
                    var chunk = mainPart.AddAlternativeFormatImportPart(
                        AlternativeFormatImportPartType.Html, altChunkId);

                    using (var chunkStream = chunk.GetStream(FileMode.Create, FileAccess.Write))
                    {
                        byte[] htmlBytes = Encoding.UTF8.GetBytes(htmlBuilder.ToString());
                        chunkStream.Write(htmlBytes, 0, htmlBytes.Length);
                    }

                    var altChunk = new AltChunk { Id = altChunkId };
                    mainPart.Document.Body!.AppendChild(altChunk);

                    // After each page (except the last), insert a hard Word page break
                    if (i < orderedRows.Count - 1)
                    {
                        var pageBreakPara = new Paragraph(
                            new Run(
                                new RunProperties(
                                    new LastRenderedPageBreak()
                                ),
                                new Break { Type = BreakValues.Page }
                            )
                        );
                        mainPart.Document.Body!.AppendChild(pageBreakPara);
                    }
                }

                mainPart.Document.Save();
            }

            return ms.ToArray();
        }
    }
}