using HtmlAgilityPack;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Data;

namespace OCR_BACKEND.Controllers
{
    public static class DocumentPdfGenerator
    {
        public static byte[] Generate(DataTable pages, int documentId, string documentName)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var orderedRows = pages.AsEnumerable()
                .OrderBy(r => Convert.ToInt32(r["PageNumber"]))
                .ToList();

            var pdf = QuestPDF.Fluent.Document.Create(container =>
            {
                for (int i = 0; i < orderedRows.Count; i++)
                {
                    var row = orderedRows[i];
                    int pageNumber = Convert.ToInt32(row["PageNumber"]);
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

            col.Item().PaddingBottom(4).PaddingLeft(indentMode == "full" ? indent : 0).Element(el =>
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
                    
                    // For first-line indent, add spaces to first span
                    if (indentMode == "first-line" && indent > 0)
                    {
                        int spaceCount = (int)Math.Round(indent / 3);
                        t.Span(new string(' ', spaceCount));
                    }
                    
                    RenderInlineNodes(t, node.ChildNodes);
                });
            });
        }

        private static void RenderHeading(ColumnDescriptor col, HtmlNode node, float fontSize)
        {
            var alignment = GetAlignment(node);
            var (indent, indentMode) = GetIndentInfo(node);

            col.Item().PaddingTop(6).PaddingBottom(4).PaddingLeft(indentMode == "full" ? indent : 0).Element(el =>
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
                    
                    // For first-line indent, add spaces to first span
                    if (indentMode == "first-line" && indent > 0)
                    {
                        int spaceCount = (int)Math.Round(indent / 3);
                        t.Span(new string(' ', spaceCount));
                    }
                    
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

        // ── Inline rendering ──────────────────────────────────────────────────

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

                if (tag == "br")
                {
                    t.Line("");
                    continue;
                }

                bool isBold = bold || tag is "b" or "strong";
                bool isItalic = italic || tag is "i" or "em";
                bool isUnderline = underline || tag is "u";

                var (color, fontSize) = GetInlineStyle(node);

                if (color != null || fontSize != null || isBold || isItalic || isUnderline)
                    RenderInlineNodesStyled(t, node.ChildNodes, isBold, isItalic, isUnderline, color, fontSize ?? 10f);
                else
                    RenderInlineNodes(t, node.ChildNodes, isBold, isItalic, isUnderline);
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
                    if (color != null) { try { span = span.FontColor(color); } catch { } }
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
            var align = node.GetAttributeValue("align", "").ToLower();
            if (!string.IsNullOrEmpty(align)) return align;

            var style = node.GetAttributeValue("style", "");
            var match = System.Text.RegularExpressions.Regex.Match(
                style, @"text-align\s*:\s*(\w+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToLower() : "left";
        }

        private static (float indent, string mode) GetIndentInfo(HtmlNode node)
        {
            var style = node.GetAttributeValue("style", "");
            var dataIndentMode = node.GetAttributeValue("data-indent-mode", "first-line");
            
            // Check for text-indent in style (first-line indentation)
            var textIndentMatch = System.Text.RegularExpressions.Regex.Match(
                style,
                @"text-indent\s*:\s*(?<num>[\d.]+)\s*(?<unit>em|rem|px)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (textIndentMatch.Success &&
                float.TryParse(textIndentMatch.Groups["num"].Value, out var textIndentValue))
            {
                var unit = textIndentMatch.Groups["unit"].Value.ToLowerInvariant();
                var indent = unit switch
                {
                    "em" or "rem" => textIndentValue * 12f,
                    _ => textIndentValue * 0.75f
                };
                return (indent, "first-line");
            }
            
            // Check for margin-left or padding-left in style (full paragraph indentation)
            var styleMatch = System.Text.RegularExpressions.Regex.Match(
                style,
                @"(?:margin-left|padding-left)\s*:\s*(?<num>[\d.]+)\s*(?<unit>px|em|rem|%)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (styleMatch.Success &&
                float.TryParse(styleMatch.Groups["num"].Value, out var value))
            {
                var unit = styleMatch.Groups["unit"].Value.ToLowerInvariant();
                var indent = unit switch
                {
                    "em" or "rem" => value * 12f,
                    "%" => value * 0.5f,
                    _ => value * 0.75f
                };
                // Margin-left is full paragraph indentation
                return (indent, "full");
            }

            // Check for indent classes
            var cls = node.GetAttributeValue("class", "");
            var classMatch = System.Text.RegularExpressions.Regex.Match(
                cls,
                @"\bql-indent-(?<level>\d+)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (classMatch.Success &&
                int.TryParse(classMatch.Groups["level"].Value, out var level))
                return (Math.Max(0, level) * 18f, dataIndentMode == "full" ? "full" : "first-line");

            return (0f, "first-line");
        }

        private static float GetIndentPadding(HtmlNode node)
        {
            var (indent, _) = GetIndentInfo(node);
            return indent;
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
