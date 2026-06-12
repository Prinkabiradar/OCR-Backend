using HtmlAgilityPack;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Data;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OCR_BACKEND.Controllers
{
    public static class DocumentPdfGenerator
    {
        private const float MaxIndentPoints = 72f; // cap at ~1 inch
        private const int MaxFirstLineIndentSpaces = 20;

        public static byte[] Generate(DataTable pages, int documentId, string documentName)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var orderedRows = pages.AsEnumerable()
                .Where(r => TryGetPageNumber(r, out _))
                .OrderBy(r => GetPageNumberOrDefault(r))
                .ToList();

            if (orderedRows.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No valid page rows found for DocumentId {documentId}. PageNumber is missing or invalid."
                );
            }

            if (TryGenerateBrowserPdf(orderedRows, documentName, out var browserPdf))
                return browserPdf;

            var pdf = QuestPDF.Fluent.Document.Create(container =>
            {
                for (int i = 0; i < orderedRows.Count; i++)
                {
                    var row = orderedRows[i];
                    int pageNumber = GetPageNumberOrDefault(row);
                    string html = row["ExtractedText"]?.ToString() ?? string.Empty;
                    bool isFirstOcrPage = i == 0;

                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(2, Unit.Centimetre);
                        page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));

                        page.Header().Column(header =>
                        {
                            // Document title only on the very first OCR page
                            if (isFirstOcrPage)
                            {
                                header.Item().PaddingBottom(6).Column(title =>
                                {
                                    title.Item().Text(documentName).Bold().FontSize(18);
                                    title.Item().PaddingTop(4)
                                        .Text($"Generated: {DateTime.Now:dd MMM yyyy HH:mm}")
                                        .FontSize(9).FontColor(Colors.Grey.Medium);
                                });
                            }

                            // Page label shown on every PDF page of this OCR section
                            header.Item().PaddingBottom(4).Row(r =>
                            {
                                r.RelativeItem().Text($"Page {pageNumber}")
                                    .Bold().FontSize(11).FontColor(Colors.Grey.Medium);
                                r.AutoItem().Text(documentName)
                                    .FontSize(9).FontColor(Colors.Grey.Lighten1);
                            });
                            header.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                        });

                        page.Content().PaddingVertical(10).Column(col =>
                        {
                            RenderHtml(col, html);
                        });

                        page.Footer().AlignCenter().Text(text =>
                        {
                            text.Span("Page ").FontSize(9).FontColor(Colors.Grey.Medium);
                            text.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Medium);
                            text.Span(" of ").FontSize(9).FontColor(Colors.Grey.Medium);
                            text.TotalPages().FontSize(9).FontColor(Colors.Grey.Medium);
                        });
                    });
                }
            })
            .WithMetadata(new DocumentMetadata
            {
                Title = documentName,
                Author = "OCR Backend",
                Subject = documentName,
                Keywords = documentName,
                Creator = "OCR Backend"
            });

            return pdf.GeneratePdf();
        }

        private static bool TryGetPageNumber(DataRow row, out int pageNumber)
        {
            pageNumber = 0;
            if (row == null || !row.Table.Columns.Contains("PageNumber"))
                return false;

            var raw = row["PageNumber"];
            if (raw == null || raw == DBNull.Value)
                return false;

            return int.TryParse(raw.ToString(), out pageNumber);
        }

        private static int GetPageNumberOrDefault(DataRow row)
        {
            return TryGetPageNumber(row, out var pageNumber) ? pageNumber : int.MaxValue;
        }

        private static bool TryGenerateBrowserPdf(
            IReadOnlyList<DataRow> orderedRows,
            string documentName,
            out byte[] pdfBytes)
        {
            pdfBytes = Array.Empty<byte>();
            var chromePath = ResolveChromePath();
            if (chromePath == null)
            {
                Console.WriteLine("[DocumentPdfGenerator] Chrome not found. Falling back to QuestPDF renderer.");
                return false;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), $"ocr-pdf-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            var htmlPath = Path.Combine(tempDir, "document.html");
            var pdfPath = Path.Combine(tempDir, "document.pdf");

            try
            {
                File.WriteAllText(htmlPath, BuildBrowserPdfHtml(orderedRows, documentName), Encoding.UTF8);

                var startInfo = new ProcessStartInfo
                {
                    FileName = chromePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };

                startInfo.ArgumentList.Add("--headless=new");
                startInfo.ArgumentList.Add("--disable-gpu");
                startInfo.ArgumentList.Add("--no-sandbox");
                startInfo.ArgumentList.Add("--disable-dev-shm-usage");
                startInfo.ArgumentList.Add("--run-all-compositor-stages-before-draw");
                startInfo.ArgumentList.Add("--virtual-time-budget=3000");
                startInfo.ArgumentList.Add($"--print-to-pdf={pdfPath}");
                startInfo.ArgumentList.Add("--print-to-pdf-no-header");
                startInfo.ArgumentList.Add(Path.GetFullPath(htmlPath));

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    Console.WriteLine("[DocumentPdfGenerator] Chrome process did not start. Falling back to QuestPDF renderer.");
                    return false;
                }

                if (!process.WaitForExit(30000))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    Console.WriteLine("[DocumentPdfGenerator] Chrome PDF render timed out. Falling back to QuestPDF renderer.");
                    return false;
                }

                if (process.ExitCode != 0 || !File.Exists(pdfPath))
                {
                    var error = process.StandardError.ReadToEnd();
                    Console.WriteLine($"[DocumentPdfGenerator] Chrome PDF render failed. ExitCode={process.ExitCode}. Error={error}");
                    return false;
                }

                pdfBytes = File.ReadAllBytes(pdfPath);
                Console.WriteLine("[DocumentPdfGenerator] PDF generated with Chrome HTML renderer.");
                return pdfBytes.Length > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DocumentPdfGenerator] Chrome PDF render exception: {ex.Message}. Falling back to QuestPDF renderer.");
                return false;
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        private static string? ResolveChromePath()
        {
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("CHROME_BIN"),
                "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
                "/usr/bin/google-chrome",
                "/usr/bin/google-chrome-stable",
                "/usr/bin/chromium",
                "/usr/bin/chromium-browser",
                "google-chrome",
                "google-chrome-stable",
                "chromium",
                "chromium-browser"
            };

            return candidates.FirstOrDefault(path =>
                !string.IsNullOrWhiteSpace(path) &&
                (File.Exists(path) || path.IndexOf(Path.DirectorySeparatorChar) < 0));
        }

        private static string BuildBrowserPdfHtml(IReadOnlyList<DataRow> orderedRows, string documentName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!doctype html>");
            sb.AppendLine("<html>");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"utf-8\">");
            sb.AppendLine("<title>");
            sb.Append(WebUtility.HtmlEncode(documentName));
            sb.AppendLine("</title>");
            sb.AppendLine("<style>");
            sb.AppendLine(@"
@page { size: A4; margin: 20mm; }
* { box-sizing: border-box; }
html, body { margin: 0; padding: 0; }
body {
  font-family: Arial, Helvetica, sans-serif;
  font-size: 16px;
  line-height: 1.5;
  color: #1a2234;
  background: #fff;
}
.ocr-page {
  height: calc(297mm - 40mm);
  max-height: calc(297mm - 40mm);
  break-after: page;
  page-break-after: always;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.ocr-page:last-child {
  break-after: auto;
  page-break-after: auto;
}
.ocr-title {
  margin-bottom: 6px;
}
.ocr-title h1 {
  margin: 0;
  font-size: 18px;
  line-height: 1.25;
  color: #111827;
}
.ocr-generated {
  margin-top: 4px;
  font-size: 9px;
  color: #808995;
}
.ocr-header {
  margin-bottom: 10px;
}
.ocr-page-row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 16px;
  margin-bottom: 4px;
  font-size: 11px;
  color: #808995;
}
.ocr-page-number {
  font-weight: 700;
}
.ocr-document-name {
  color: #b2bac6;
  font-size: 9px;
  text-align: right;
}
.ocr-rule {
  height: 1px;
  background: #e5e7eb;
}
.ocr-content-frame {
  width: 100%;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}
.ck-content {
  width: 100%;
  padding: 10px 0;
  overflow-wrap: break-word;
  word-break: normal;
  transform-origin: top left;
}
.ck-content p {
  margin: 0 0 1.1em;
}
.ck-content h1,
.ck-content h2,
.ck-content h3,
.ck-content h4,
.ck-content h5,
.ck-content h6 {
  margin: 0.65em 0 0.35em;
  line-height: 1.25;
  font-weight: 700;
}
.ck-content h1 { font-size: 2em; }
.ck-content h2 { font-size: 1.5em; }
.ck-content h3 { font-size: 1.25em; }
.ck-content ul,
.ck-content ol {
  margin: 0 0 1.1em 1.5em;
  padding-left: 1.5em;
}
.ck-content blockquote {
  margin: 1em 0;
  padding-left: 1em;
  border-left: 4px solid #d6d9df;
}
.ck-content table {
  border-collapse: collapse;
  width: 100%;
  margin: 1em 0;
}
.ck-content table td,
.ck-content table th {
  border: 1px solid #d6d9df;
  padding: 6px 8px;
}
.ck-content .text-tiny { font-size: 0.7em; }
.ck-content .text-small { font-size: 0.85em; }
.ck-content .text-big { font-size: 1.4em; }
.ck-content .text-huge { font-size: 1.8em; }
.ocr-footer {
  margin-top: auto;
  padding-top: 8px;
  text-align: center;
  font-size: 9px;
  color: #808995;
}
");
            sb.AppendLine("</style>");
            sb.AppendLine("<script>");
            sb.AppendLine(@"
function fitOcrPages() {
  var frames = Array.from(document.querySelectorAll('.ocr-content-frame'));
  var documentScale = 1;
  var editorToPdfPxScale = 0.42;

  frames.forEach(function(frame) {
    var content = frame.querySelector('.ck-content');
    if (!content || content.dataset.pdfLayoutNormalized === 'true') return;

    Array.from(content.querySelectorAll('[style]')).forEach(function(element) {
      ['marginLeft', 'marginRight', 'paddingLeft', 'paddingRight', 'textIndent'].forEach(function(propertyName) {
        var value = element.style[propertyName];
        if (!value || value.indexOf('px') === -1) return;

        var numericValue = parseFloat(value);
        if (!isFinite(numericValue) || numericValue <= 0) return;

        element.style[propertyName] = (numericValue * editorToPdfPxScale) + 'px';
      });
    });

    content.dataset.pdfLayoutNormalized = 'true';
  });

  frames.forEach(function(frame) {
    var content = frame.querySelector('.ck-content');
    if (!content) return;

    content.style.transform = '';
    content.style.width = '100%';
  });

  frames.forEach(function(frame) {
    var content = frame.querySelector('.ck-content');
    if (!content) return;

    var frameHeight = Math.max(1, frame.clientHeight);
    var frameWidth = Math.max(1, frame.clientWidth);
    var contentHeight = Math.max(1, content.scrollHeight);
    var contentWidth = Math.max(1, content.scrollWidth);

    documentScale = Math.min(
      documentScale,
      frameHeight / contentHeight,
      frameWidth / contentWidth
    );
  });

  documentScale = Math.min(1, Math.max(0.32, documentScale));

  frames.forEach(function(frame) {
    var content = frame.querySelector('.ck-content');
    if (!content) return;

    if (documentScale < 1) {
      content.style.transform = 'scale(' + documentScale + ')';
      content.style.width = (100 / documentScale) + '%';
    }
  });
}
window.addEventListener('load', fitOcrPages);
window.addEventListener('beforeprint', fitOcrPages);
setTimeout(fitOcrPages, 50);
setTimeout(fitOcrPages, 250);
");
            sb.AppendLine("</script>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");

            for (var i = 0; i < orderedRows.Count; i++)
            {
                var row = orderedRows[i];
                var pageNumber = GetPageNumberOrDefault(row);
                var html = DecodeEscapedHtmlMarkup(row["ExtractedText"]?.ToString() ?? string.Empty);
                sb.AppendLine("<section class=\"ocr-page\">");
                if (i == 0)
                {
                    sb.AppendLine("<div class=\"ocr-title\">");
                    sb.Append("<h1>");
                    sb.Append(WebUtility.HtmlEncode(documentName));
                    sb.AppendLine("</h1>");
                    sb.Append("<div class=\"ocr-generated\">Generated: ");
                    sb.Append(WebUtility.HtmlEncode(DateTime.Now.ToString("dd MMM yyyy HH:mm")));
                    sb.AppendLine("</div>");
                    sb.AppendLine("</div>");
                }
                sb.AppendLine("<header class=\"ocr-header\">");
                sb.AppendLine("<div class=\"ocr-page-row\">");
                sb.Append("<span class=\"ocr-page-number\">Page ");
                sb.Append(WebUtility.HtmlEncode(pageNumber.ToString()));
                sb.AppendLine("</span>");
                sb.Append("<span class=\"ocr-document-name\">");
                sb.Append(WebUtility.HtmlEncode(documentName));
                sb.AppendLine("</span>");
                sb.AppendLine("</div>");
                sb.AppendLine("<div class=\"ocr-rule\"></div>");
                sb.AppendLine("</header>");
                sb.AppendLine("<div class=\"ocr-content-frame\">");
                sb.AppendLine("<main class=\"ck-content\">");
                sb.AppendLine(html);
                sb.AppendLine("</main>");
                sb.AppendLine("</div>");
                sb.Append("<footer class=\"ocr-footer\">Page ");
                sb.Append(i + 1);
                sb.Append(" of ");
                sb.Append(orderedRows.Count);
                sb.AppendLine("</footer>");
                sb.AppendLine("</section>");
            }

            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            return sb.ToString();
        }

        // ── HTML → QuestPDF renderer ──────────────────────────────────────────

        private static void RenderHtml(ColumnDescriptor col, string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return;

            html = DecodeEscapedHtmlMarkup(html);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            foreach (var node in doc.DocumentNode.ChildNodes)
                RenderNode(col, node);
        }

        private static void RenderNode(ColumnDescriptor col, HtmlNode node)
        {
            if (node.NodeType == HtmlNodeType.Text)
            {
                var text = NormalizeText(node.InnerText);

                if (!string.IsNullOrEmpty(text))
                    col.Item().Text(t => t.Span(text).FontSize(10));
                return;
            }

            var tag = node.Name.ToLower();

            switch (tag)
            {
                case "p":
                case "div":
                    RenderBlockElement(col, node);
                    return;

                case "h1": RenderHeading(col, node, 20); return;
                case "h2": RenderHeading(col, node, 17); return;
                case "h3": RenderHeading(col, node, 14); return;
                case "h4":
                case "h5":
                case "h6": RenderHeading(col, node, 12); return;

                case "ul":
                case "ol":
                    RenderList(col, node, tag == "ol");
                    return;

                case "li":
                    RenderBlockElement(col, node, prefix: "• ");
                    return;

                case "br":
                    col.Item().PaddingBottom(4).Text("");
                    return;

                case "table":
                    RenderTable(col, node);
                    return;

                default:
                    foreach (var child in node.ChildNodes)
                        RenderNode(col, child);
                    return;
            }
        }

        private static void RenderBlockElement(ColumnDescriptor col, HtmlNode node, string prefix = "")
        {
            var alignment = GetAlignment(node);
            var (indent, indentMode) = GetIndentInfo(node);

            // For first-line indent, don't use PaddingLeft, add spaces to first span instead
            var paddingLeft = (indentMode == "full" && indent > 0) ? indent : 0f;

            col.Item().PaddingBottom(4).PaddingLeft(paddingLeft).Element(el =>
            {
                el.Text(t =>
                {
                    ApplyTextAlignment(t, alignment);

                    if (!string.IsNullOrEmpty(prefix))
                        t.Span(prefix);
                    
                    // For first-line indent only, add non-breaking spaces so indentation
                    // survives HTML/PDF whitespace normalization.
                    if (indentMode == "first-line" && indent > 0)
                    {
                        int spaceCount = Math.Max(1, Math.Min(MaxFirstLineIndentSpaces, (int)Math.Round(indent / 2.5f)));
                        t.Span(new string('\u00A0', spaceCount));
                    }
                    
                    RenderInlineNodes(t, node.ChildNodes);
                });
            });
        }

        private static void RenderHeading(ColumnDescriptor col, HtmlNode node, float fontSize)
        {
            var alignment = GetAlignment(node);
            var (indent, indentMode) = GetIndentInfo(node);

            if (indent > 0)
            {
                // Use proper paragraph indentation with QuestPDF
                col.Item().PaddingTop(6).PaddingBottom(4).PaddingLeft(indent).Element(el =>
                {
                    el.Text(t =>
                    {
                        ApplyTextAlignment(t, alignment);
                        t.DefaultTextStyle(s => s.Bold().FontSize(fontSize));
                        RenderInlineNodes(t, node.ChildNodes);
                    });
                });
            }
            else
            {
                col.Item().PaddingTop(6).PaddingBottom(4).Element(el =>
                {
                    el.Text(t =>
                    {
                        ApplyTextAlignment(t, alignment);
                        t.DefaultTextStyle(s => s.Bold().FontSize(fontSize));
                        RenderInlineNodes(t, node.ChildNodes);
                    });
                });
            }
        }

        private static void RenderList(ColumnDescriptor col, HtmlNode node, bool ordered)
        {
            int index = 1;
            foreach (var li in node.ChildNodes.Where(n => n.Name.ToLower() == "li"))
            {
                string bullet = ordered ? $"{index++}. " : "• ";
                col.Item().PaddingLeft(16).PaddingBottom(2).Text(t =>
                {
                    t.Span(bullet);
                    RenderInlineNodes(t, li.ChildNodes);
                });
            }
        }

        private static void RenderTable(ColumnDescriptor col, HtmlNode tableNode)
        {
            var rows = tableNode.SelectNodes(".//tr");
            if (rows == null) return;

            int colCount = rows.Max(r =>
                r.ChildNodes.Count(n => n.Name == "td" || n.Name == "th"));

            // Malformed table, fallback to plain text content
            if (colCount <= 0)
            {
                var text = System.Net.WebUtility.HtmlDecode(tableNode.InnerText ?? string.Empty);
                text = NormalizeText(text);
                if (!string.IsNullOrWhiteSpace(text))
                    col.Item().PaddingBottom(6).Text(t => t.Span(text).FontSize(10));
                return;
            }

            col.Item().PaddingBottom(8).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    for (int i = 0; i < colCount; i++)
                        cols.RelativeColumn();
                });

                bool isFirst = true;
                foreach (var tr in rows)
                {
                    bool isHeader = isFirst ||
                        tr.ChildNodes.Any(n => n.Name.ToLower() == "th");

                    foreach (var cell in tr.ChildNodes
                        .Where(n => n.Name == "td" || n.Name == "th"))
                    {
                        table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .Padding(4).Text(t =>
                            {
                                if (isHeader)
                                    t.DefaultTextStyle(s => s.Bold());
                                RenderInlineNodes(t, cell.ChildNodes);
                            });
                    }
                    isFirst = false;
                }
            });
        }

        // ── Inline rendering ──────────────────────────────────────────────────

        private static void RenderInlineNodes(TextDescriptor t, HtmlNodeCollection nodes,
            bool bold = false, bool italic = false, bool underline = false, bool strikethrough = false)
        {
            if (nodes == null) return;

            foreach (var node in nodes)
            {
                if (node.NodeType == HtmlNodeType.Text)
                {
                    var text = NormalizeText(node.InnerText);
                    if (string.IsNullOrEmpty(text)) continue;

                    var span = t.Span(text).FontSize(10);
                    if (bold) span = span.Bold();
                    if (italic) span = span.Italic();
                    if (underline) span = span.Underline();
                    if (strikethrough) span = span.Strikethrough();
                    continue;
                }

                var tag = node.Name.ToLower();

                if (tag == "br")
                {
                    t.Line("");
                    continue;
                }

                bool isBold = bold || tag is "b" or "strong";
                bool isItalic = italic || tag is "i" or "em";
                bool isUnderline = underline || tag is "u";
                bool isStrikethrough = strikethrough || tag is "s" or "strike" or "del";

                var style = GetInlineStyle(node);
                isBold = isBold || style.Bold;
                isItalic = isItalic || style.Italic;
                isUnderline = isUnderline || style.Underline;
                isStrikethrough = isStrikethrough || style.Strikethrough;

                if (style.Color != null || style.FontSize != null || style.FontFamily != null ||
                    isBold || isItalic || isUnderline || isStrikethrough)
                    RenderInlineNodesStyled(t, node.ChildNodes, isBold, isItalic, isUnderline,
                        isStrikethrough, style.Color, style.FontSize ?? 10f, style.FontFamily);
                else
                    RenderInlineNodes(t, node.ChildNodes, isBold, isItalic, isUnderline, isStrikethrough);
            }
        }

        private static void RenderInlineNodesStyled(TextDescriptor t, HtmlNodeCollection nodes,
            bool bold, bool italic, bool underline, bool strikethrough,
            string? color, float fontSize, string? fontFamily)
        {
            if (nodes == null) return;

            foreach (var node in nodes)
            {
                if (node.NodeType == HtmlNodeType.Text)
                {
                    var text = NormalizeText(node.InnerText);
                    if (string.IsNullOrEmpty(text)) continue;

                    var span = t.Span(text).FontSize(fontSize);
                    if (bold) span = span.Bold();
                    if (italic) span = span.Italic();
                    if (underline) span = span.Underline();
                    if (strikethrough) span = span.Strikethrough();
                    if (!string.IsNullOrWhiteSpace(fontFamily))
                    {
                        try { span = span.FontFamily(fontFamily); } catch { }
                    }
                    if (color != null) { try { span = span.FontColor(color); } catch { } }
                }
                else
                {
                    var tag = node.Name.ToLower();
                    var style = GetInlineStyle(node);
                    RenderInlineNodesStyled(t, node.ChildNodes,
                        bold || tag is "b" or "strong" || style.Bold,
                        italic || tag is "i" or "em" || style.Italic,
                        underline || tag is "u" || style.Underline,
                        strikethrough || tag is "s" or "strike" or "del" || style.Strikethrough,
                        style.Color ?? color,
                        style.FontSize ?? fontSize,
                        style.FontFamily ?? fontFamily);
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string GetAlignment(HtmlNode node)
        {
            var align = node.GetAttributeValue("align", "").ToLower();
            if (!string.IsNullOrEmpty(align)) return align;

            var style = node.GetAttributeValue("style", "");
            var match = System.Text.RegularExpressions.Regex.Match(
                style, @"text-align\s*:\s*(\w+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToLower() : "left";
        }

        private static void ApplyTextAlignment(TextDescriptor text, string alignment)
        {
            switch (alignment)
            {
                case "center":
                    text.AlignCenter();
                    break;
                case "right":
                    text.AlignRight();
                    break;
                case "justify":
                    text.Justify();
                    break;
                default:
                    text.AlignLeft();
                    break;
            }
        }

        private static (float indent, string mode) GetIndentInfo(HtmlNode node)
        {
            var style = node.GetAttributeValue("style", "");
            var dataIndentMode = GetIndentMode(node, style);
            
            // Check for indent="X" attribute from frontend
            var indentAttr = node.GetAttributeValue("indent", "");
            if (!string.IsNullOrWhiteSpace(indentAttr) && int.TryParse(indentAttr, out var indentLevel))
            {
                var indentValue = indentLevel * 18f;
                return (Math.Min(MaxIndentPoints, Math.Max(0, indentValue)), dataIndentMode);
            }
            
            // Check for data-indent="X" attribute from frontend  
            var dataIndentAttr = node.GetAttributeValue("data-indent", "");
            if (!string.IsNullOrWhiteSpace(dataIndentAttr) && int.TryParse(dataIndentAttr, out var dataIndentLevel))
            {
                var indentValue = dataIndentLevel * 18f;
                return (Math.Min(MaxIndentPoints, Math.Max(0, indentValue)), dataIndentMode);
            }
            
            // First check for explicit data-indent-mode attribute
            if (!string.IsNullOrWhiteSpace(dataIndentMode) && (dataIndentMode == "full" || dataIndentMode == "first-line"))
            {
                // Try to extract indent value from style first
                var indentValue = ExtractIndentValue(style);
                if (indentValue > 0)
                {
                    return (Math.Min(MaxIndentPoints, Math.Max(0, indentValue)), dataIndentMode);
                }
            }
            
            // Check for text-indent in style (first-line indentation)
            var textIndentMatch = System.Text.RegularExpressions.Regex.Match(
                style,
                @"text-indent\s*:\s*(?<num>[\d.]+(?:\.\d+)?)\s*(?<unit>em|rem|px)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (textIndentMatch.Success &&
                float.TryParse(textIndentMatch.Groups["num"].Value, out var textIndentValue))
            {
                var unit = textIndentMatch.Groups["unit"].Value.ToLowerInvariant();
                var indent = unit switch
                {
                    "em" or "rem" => textIndentValue * 12f,
                    "px" => textIndentValue * 0.75f,
                    _ => textIndentValue * 0.75f
                };
                return (Math.Min(MaxIndentPoints, Math.Max(0, indent)), "first-line");
            }
            
            // Check for margin-left in style (full paragraph indentation)
            var marginLeftMatch = System.Text.RegularExpressions.Regex.Match(
                style,
                @"margin-left\s*:\s*(?<num>[\d.]+(?:\.\d+)?)\s*(?<unit>px|em|rem|%)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (marginLeftMatch.Success &&
                float.TryParse(marginLeftMatch.Groups["num"].Value, out var marginValue))
            {
                var unit = marginLeftMatch.Groups["unit"].Value.ToLowerInvariant();
                var indent = unit switch
                {
                    "em" or "rem" => marginValue * 12f,
                    "%" => marginValue * 0.5f,
                    "px" => marginValue * 0.75f,
                    _ => marginValue * 0.75f
                };
                return (Math.Min(MaxIndentPoints, Math.Max(0, indent)), "full");
            }

            // Check for indent classes (Quill-style)
            var cls = node.GetAttributeValue("class", "");
            var classMatch = System.Text.RegularExpressions.Regex.Match(
                cls,
                @"\bql-indent-(?<level>\d+)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (classMatch.Success &&
                int.TryParse(classMatch.Groups["level"].Value, out var level))
                return (Math.Min(MaxIndentPoints, Math.Max(0, level) * 18f), "full");

            return (0f, "full");
        }

        private static string GetIndentMode(HtmlNode node, string style)
        {
            // Prefer explicit attribute when present.
            var attrMode = node.GetAttributeValue("data-indent-mode", "").Trim().ToLowerInvariant();
            if (attrMode == "first-line" || attrMode == "full")
                return attrMode;

            // Editor-style indent attrs without explicit mode should behave as first-line.
            var indentAttr = node.GetAttributeValue("indent", "");
            var dataIndentAttr = node.GetAttributeValue("data-indent", "");
            if (!string.IsNullOrWhiteSpace(indentAttr) || !string.IsNullOrWhiteSpace(dataIndentAttr))
                return "first-line";

            // Fallback inference for older/dirty HTML.
            var textIndentMatch = System.Text.RegularExpressions.Regex.Match(
                style,
                @"text-indent\s*:\s*(?<num>[\d.]+(?:\.\d+)?)\s*(?<unit>em|rem|px)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (textIndentMatch.Success &&
                float.TryParse(textIndentMatch.Groups["num"].Value, out var textIndentValue) &&
                textIndentValue > 0)
                return "first-line";

            var marginLeftMatch = System.Text.RegularExpressions.Regex.Match(
                style,
                @"margin-left\s*:\s*(?<num>[\d.]+(?:\.\d+)?)\s*(?<unit>px|em|rem|%)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (marginLeftMatch.Success &&
                float.TryParse(marginLeftMatch.Groups["num"].Value, out var marginValue) &&
                marginValue > 0)
                return "full";

            return "full";
        }

        private static float ExtractIndentValue(string style)
        {
            if (string.IsNullOrWhiteSpace(style))
                return 0f;

            // Try margin-left first
            var marginMatch = System.Text.RegularExpressions.Regex.Match(
                style,
                @"margin-left\s*:\s*(?<num>[\d.]+(?:\.\d+)?)\s*(?<unit>px|em|rem)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (marginMatch.Success && float.TryParse(marginMatch.Groups["num"].Value, out var mValue))
            {
                var unit = marginMatch.Groups["unit"].Value.ToLowerInvariant();
                return unit switch
                {
                    "em" or "rem" => mValue * 12f,
                    "px" => mValue * 0.75f,
                    _ => mValue * 0.75f
                };
            }

            // Try text-indent
            var textMatch = System.Text.RegularExpressions.Regex.Match(
                style,
                @"text-indent\s*:\s*(?<num>[\d.]+(?:\.\d+)?)\s*(?<unit>em|rem|px)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (textMatch.Success && float.TryParse(textMatch.Groups["num"].Value, out var tValue))
            {
                var unit = textMatch.Groups["unit"].Value.ToLowerInvariant();
                return unit switch
                {
                    "em" or "rem" => tValue * 12f,
                    "px" => tValue * 0.75f,
                    _ => tValue * 0.75f
                };
            }

            return 0f;
        }

        private static float GetIndentPadding(HtmlNode node)
        {
            var (indent, _) = GetIndentInfo(node);
            return indent;
        }

        private static string NormalizeText(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // Preserve all spaces exactly as they are
            text = WebUtility.HtmlDecode(text);

            // Convert normal spaces to non-breaking spaces
            // so QuestPDF doesn't collapse multiple spaces
            text = Regex.Replace(
                text,
                @" {2,}",
                match => new string('\u00A0', match.Value.Length)
            );

            return text;
        }

        private static string DecodeEscapedHtmlMarkup(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            var decoded = html;

            for (var i = 0; i < 5; i++)
            {
                if (!ContainsEscapedHtmlTag(decoded))
                    break;

                var next = WebUtility.HtmlDecode(decoded);
                if (string.Equals(next, decoded, StringComparison.Ordinal))
                    break;

                decoded = next;
            }

            return decoded;
        }
        private static bool ContainsEscapedHtmlTag(string value)
        {
            return Regex.IsMatch(
                value,
                @"&lt;\s*/?\s*[a-z][\w:-]*(?:\s|/|&gt;)",
                RegexOptions.IgnoreCase);
        }

        private sealed record InlineStyle(
            string? Color,
            float? FontSize,
            string? FontFamily,
            bool Bold,
            bool Italic,
            bool Underline,
            bool Strikethrough);

        private static InlineStyle GetInlineStyle(HtmlNode node)
        {
            var style = node.GetAttributeValue("style", "");
            string? color = null;
            float? fontSize = null;
            string? fontFamily = null;
            bool bold = false;
            bool italic = false;
            bool underline = false;
            bool strikethrough = false;

            var colorMatch = System.Text.RegularExpressions.Regex.Match(
                style, @"color\s*:\s*([^;]+)");
            if (colorMatch.Success)
                color = colorMatch.Groups[1].Value.Trim();

            var sizeMatch = System.Text.RegularExpressions.Regex.Match(
                style,
                @"font-size\s*:\s*(?<num>[\d.]+)\s*(?<unit>px|pt|em|rem|%)?",
                RegexOptions.IgnoreCase);
            if (sizeMatch.Success && float.TryParse(sizeMatch.Groups["num"].Value, out float fs))
            {
                var unit = sizeMatch.Groups["unit"].Value.ToLowerInvariant();
                fontSize = unit switch
                {
                    "em" or "rem" => fs * 10f,
                    "%" => 10f * fs / 100f,
                    _ => fs
                };
            }

            var familyMatch = Regex.Match(
                style,
                @"font-family\s*:\s*([^;]+)",
                RegexOptions.IgnoreCase);
            if (familyMatch.Success)
            {
                fontFamily = familyMatch.Groups[1].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().Trim('\'', '"'))
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            }

            var weightMatch = Regex.Match(
                style,
                @"font-weight\s*:\s*(bold|bolder|[6-9]00)",
                RegexOptions.IgnoreCase);
            bold = weightMatch.Success;

            var fontStyleMatch = Regex.Match(
                style,
                @"font-style\s*:\s*(italic|oblique)",
                RegexOptions.IgnoreCase);
            italic = fontStyleMatch.Success;

            var decorationMatch = Regex.Match(
                style,
                @"text-decoration(?:-line)?\s*:\s*([^;]+)",
                RegexOptions.IgnoreCase);
            if (decorationMatch.Success)
            {
                var decoration = decorationMatch.Groups[1].Value.ToLowerInvariant();
                underline = decoration.Contains("underline");
                strikethrough = decoration.Contains("line-through");
            }

            return new InlineStyle(
                color,
                fontSize,
                fontFamily,
                bold,
                italic,
                underline,
                strikethrough);
        }
    }
}
