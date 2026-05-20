using System.Net;
using System.Text.RegularExpressions;
using System.Linq;

namespace OCR_BACKEND.Services
{
    public static class ExtractedTextSanitizer
    {
        public static string ToPlainBlackFriendlyText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var cleaned = value
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\r", "\r", StringComparison.Ordinal);

            cleaned = WebUtility.HtmlDecode(cleaned);
            cleaned = Regex.Replace(cleaned, @"</?(html|head|body)\b[^>]*>", string.Empty, RegexOptions.IgnoreCase);

            // Keep only safe style declarations needed for editor formatting persistence
            // (alignment + indentation + font formatting), drop everything else.
            cleaned = Regex.Replace(
                cleaned,
                @"\sstyle\s*=\s*(['""])(.*?)\1",
                match =>
                {
                    var styleValue = match.Groups[2].Value;
                    var allowed = styleValue
                        .Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Select(part => part.Trim())
                        .Where(part =>
                            // Text alignment
                            Regex.IsMatch(part, @"^text-align\s*:\s*(left|right|center|justify)\s*!?$", RegexOptions.IgnoreCase) ||
                            // Indentation via margins (ngx-editor, Quill)
                            Regex.IsMatch(part, @"^margin-left\s*:\s*[\d.]+(px|em|rem|%)\s*!?$", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(part, @"^margin-right\s*:\s*[\d.]+(px|em|rem|%)\s*!?$", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(part, @"^margin-inline-start\s*:\s*[\d.]+(px|em|rem|%)\s*!?$", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(part, @"^margin-inline-end\s*:\s*[\d.]+(px|em|rem|%)\s*!?$", RegexOptions.IgnoreCase) ||
                            // Indentation via padding
                            Regex.IsMatch(part, @"^padding-left\s*:\s*[\d.]+(px|em|rem|%)\s*!?$", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(part, @"^padding-right\s*:\s*[\d.]+(px|em|rem|%)\s*!?$", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(part, @"^padding-inline-start\s*:\s*[\d.]+(px|em|rem|%)\s*!?$", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(part, @"^padding-inline-end\s*:\s*[\d.]+(px|em|rem|%)\s*!?$", RegexOptions.IgnoreCase) ||
                            // Text indent (first line indent)
                            Regex.IsMatch(part, @"^text-indent\s*:\s*[\d.-]+(px|em|rem|%)\s*!?$", RegexOptions.IgnoreCase) ||
                            // Font styles (CKEditor font family/size)
                            Regex.IsMatch(part, @"^font-size\s*:\s*[\d.]+(px|em|rem|pt|%)\s*!?$", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(part, @"^font-family\s*:\s*['\""""\w\-\s,]+\s*!?$", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(part, @"^font-weight\s*:\s*(normal|bold|bolder|lighter|[1-9]00)\s*!?$", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(part, @"^font-style\s*:\s*(normal|italic|oblique)\s*!?$", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(part, @"^text-decoration\s*:\s*(none|underline|line-through|overline)(\s+(solid|double|dotted|dashed|wavy))?(\s+#[0-9a-fA-F]{3,8})?\s*!?$", RegexOptions.IgnoreCase) ||
                            // Color styles
                            Regex.IsMatch(part, @"^color\s*:\s*", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(part, @"^background-color\s*:\s*", RegexOptions.IgnoreCase))
                        .ToList();

                    return allowed.Count == 0
                        ? string.Empty
                        : $" style=\"{string.Join("; ", allowed)}\"";
                },
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Keep only known safe formatting classes used by rich text editors.
            // Supports: Quill (ql-*), ngx-editor, CKEditor and generic alignment/indent.
            cleaned = Regex.Replace(
                cleaned,
                @"\sclass\s*=\s*(['""])(.*?)\1",
                match =>
                {
                    var classValue = match.Groups[2].Value;
                    var allowed = classValue
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Where(c =>
                            // Quill alignment classes
                            Regex.IsMatch(c, @"^ql-align-(left|right|center|justify)$", RegexOptions.IgnoreCase) ||
                            // Quill indent classes (ql-indent-1, ql-indent-2, etc.)
                            Regex.IsMatch(c, @"^ql-indent-\d+$", RegexOptions.IgnoreCase) ||
                            // ngx-editor indent classes
                            Regex.IsMatch(c, @"^indent-\d+$", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(c, @"^editor-indent-\d+$", RegexOptions.IgnoreCase) ||
                            // ProseMirror indent classes
                            Regex.IsMatch(c, @"^pm-indent-\d+$", RegexOptions.IgnoreCase) ||
                            // Generic indent/level classes
                            Regex.IsMatch(c, @"^(indent|level)-\d+$", RegexOptions.IgnoreCase) ||
                            // Generic alignment classes
                            Regex.IsMatch(c, @"^text-(left|right|center|justify)$", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(c, @"^align-(left|right|center|justify)$", RegexOptions.IgnoreCase) ||
                            // CKEditor font classes
                            Regex.IsMatch(c, @"^text-(tiny|small|big|huge)$", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(c, @"^font-[\w\-]+$", RegexOptions.IgnoreCase))
                        .ToList();

                    return allowed.Count == 0
                        ? string.Empty
                        : $" class=\"{string.Join(" ", allowed)}\"";
                },
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Preserve blockquote, lists, and list items (used for hierarchical indentation)
            // Already preserved by not removing them

            // Remove only truly unsafe elements
            cleaned = Regex.Replace(cleaned, @"</?font\b[^>]*>", string.Empty, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"<script\b[^>]*>.*?</script>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            cleaned = Regex.Replace(cleaned, @"<iframe\b[^>]*>.*?</iframe>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            cleaned = Regex.Replace(cleaned, @"<style\b[^>]*>.*?</style>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Preserve data attributes that might be used for indentation (data-indent, data-level, etc.)
            // These are already safe and won't be removed unless explicitly stripped

            return cleaned.Trim();
        }
    }
}
