using HtmlAgilityPack;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Data;
using System.Xml;

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

                    page.Header().Column(col =>
                    {
                        col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                    });

                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        // Document title
                        col.Item().PaddingBottom(16).Column(title =>
                        {
                            title.Item().Text(documentName).Bold().FontSize(18);
                            title.Item().PaddingTop(4)
                                .Text($"Generated: {DateTime.Now:dd MMM yyyy HH:mm}")
                                .FontSize(9).FontColor(Colors.Grey.Medium);
                            title.Item().PaddingTop(8)
                                .LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                        });

                        foreach (DataRow row in pages.Rows)
                        {
                            int pageNumber = Convert.ToInt32(row["PageNumber"]);
                            string html = row["ExtractedText"]?.ToString() ?? string.Empty;

                            col.Item().PaddingBottom(20).Column(inner =>
                            {
                                inner.Item().PaddingBottom(4)
                                    .Text($"Page {pageNumber}")
                                    .Bold().FontSize(11).FontColor(Colors.Grey.Medium);

                                inner.Item().PaddingBottom(8)
                                    .LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);

                                // Render each HTML block element as its own QuestPDF item
                                RenderHtml(inner, html);
                            });
                        }
                    });

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

        // ── HTML → QuestPDF renderer ──────────────────────────────────────────

        private static void RenderHtml(ColumnDescriptor col, string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            foreach (var node in doc.DocumentNode.ChildNodes)
                RenderNode(col, node);
        }

        private static void RenderNode(ColumnDescriptor col, HtmlNode node)
        {
            if (node.NodeType == HtmlNodeType.Text)
            {
                var text = System.Net.WebUtility.HtmlDecode(node.InnerText);
                if (!string.IsNullOrWhiteSpace(text))
                    col.Item().Text(t => t.Span(text).FontSize(10));
                return;
            }

            var tag = node.Name.ToLower();

            // Block-level elements — each gets its own column item
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
                    col.Item().PaddingBottom(4).Text(""); // spacing
                    return;

                case "table":
                    RenderTable(col, node);
                    return;

                default:
                    // Recurse into unknown block wrappers
                    foreach (var child in node.ChildNodes)
                        RenderNode(col, child);
                    return;
            }
        }

        private static void RenderBlockElement(ColumnDescriptor col, HtmlNode node, string prefix = "")
        {
            // Determine text alignment from style/align attribute
            var alignment = GetAlignment(node);

            col.Item().PaddingBottom(4).Element(el =>
            {
                var aligned = alignment switch
                {
                    "center" => el.AlignCenter(),
                    "right" => el.AlignRight(),
                    _ => el.AlignLeft()
                };

                aligned.Text(t =>
                {
                    if (!string.IsNullOrEmpty(prefix))
                        t.Span(prefix);

                    RenderInlineNodes(t, node.ChildNodes);
                });
            });
        }

        private static void RenderHeading(ColumnDescriptor col, HtmlNode node, float fontSize)
        {
            var alignment = GetAlignment(node);

            col.Item().PaddingTop(6).PaddingBottom(4).Element(el =>
            {
                var aligned = alignment switch
                {
                    "center" => el.AlignCenter(),
                    "right" => el.AlignRight(),
                    _ => el.AlignLeft()
                };

                aligned.Text(t =>
                {
                    t.DefaultTextStyle(s => s.Bold().FontSize(fontSize));
                    RenderInlineNodes(t, node.ChildNodes);
                });
            });
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

            col.Item().PaddingBottom(8).Table(table =>
            {
                // Count max columns
                int colCount = rows.Max(r =>
                    r.ChildNodes.Count(n => n.Name == "td" || n.Name == "th"));

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

        // ── Inline rendering (bold, italic, underline, spans) ─────────────────

        private static void RenderInlineNodes(TextDescriptor t, HtmlNodeCollection nodes,
    bool bold = false, bool italic = false, bool underline = false)
        {
            if (nodes == null) return;

            foreach (var node in nodes)
            {
                if (node.NodeType == HtmlNodeType.Text)
                {
                    var text = System.Net.WebUtility.HtmlDecode(node.InnerText);
                    if (string.IsNullOrEmpty(text)) continue;

                    var span = t.Span(text).FontSize(10);
                    if (bold) span = span.Bold();
                    if (italic) span = span.Italic();
                    if (underline) span = span.Underline();
                    continue;
                }

                var tag = node.Name.ToLower();

                // ✅ FIX: treat <br> as a newline within the same text block
                if (tag == "br")
                {
                    t.Line(""); // emits a line break, keeping alignment intact
                    continue;
                }

                bool isBold = bold || tag is "b" or "strong";
                bool isItalic = italic || tag is "i" or "em";
                bool isUnderline = underline || tag is "u";

                var (color, fontSize) = GetInlineStyle(node);

                if (color != null || fontSize != null || isBold || isItalic || isUnderline)
                {
                    RenderInlineNodesStyled(t, node.ChildNodes,
                        isBold, isItalic, isUnderline, color, fontSize ?? 10f);
                }
                else
                {
                    RenderInlineNodes(t, node.ChildNodes, isBold, isItalic, isUnderline);
                }
            }
        }

        private static void RenderInlineNodesStyled(TextDescriptor t, HtmlNodeCollection nodes,
            bool bold, bool italic, bool underline, string? color, float fontSize)
        {
            if (nodes == null) return;

            foreach (var node in nodes)
            {
                if (node.NodeType == HtmlNodeType.Text)
                {
                    var text = System.Net.WebUtility.HtmlDecode(node.InnerText);
                    if (string.IsNullOrEmpty(text)) continue;

                    var span = t.Span(text).FontSize(fontSize);
                    if (bold) span = span.Bold();
                    if (italic) span = span.Italic();
                    if (underline) span = span.Underline();
                    if (color != null)
                    {
                        try { span = span.FontColor(color); } catch { }
                    }
                }
                else
                {
                    var tag = node.Name.ToLower();
                    RenderInlineNodesStyled(t, node.ChildNodes,
                        bold || tag is "b" or "strong",
                        italic || tag is "i" or "em",
                        underline || tag is "u",
                        color, fontSize);
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string GetAlignment(HtmlNode node)
        {
            // Check align attribute
            var align = node.GetAttributeValue("align", "").ToLower();
            if (!string.IsNullOrEmpty(align)) return align;

            // Check style="text-align: center" etc.
            var style = node.GetAttributeValue("style", "");
            var match = System.Text.RegularExpressions.Regex.Match(
                style, @"text-align\s*:\s*(\w+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToLower() : "left";
        }

        private static (string? color, float? fontSize) GetInlineStyle(HtmlNode node)
        {
            var style = node.GetAttributeValue("style", "");
            string? color = null;
            float? fontSize = null;

            var colorMatch = System.Text.RegularExpressions.Regex.Match(
                style, @"color\s*:\s*([^;]+)");
            if (colorMatch.Success)
                color = colorMatch.Groups[1].Value.Trim();

            var sizeMatch = System.Text.RegularExpressions.Regex.Match(
                style, @"font-size\s*:\s*([\d.]+)");
            if (sizeMatch.Success && float.TryParse(sizeMatch.Groups[1].Value, out float fs))
                fontSize = fs;

            return (color, fontSize);
        }
    }
}