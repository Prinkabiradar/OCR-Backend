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
            // (alignment + indentation), drop everything else.
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
                            Regex.IsMatch(part, @"^text-align\s*:\s*(left|right|center|justify)\s*$", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(part, @"^margin-left\s*:\s*[\d.]+(px|em|rem|%)\s*$", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(part, @"^padding-left\s*:\s*[\d.]+(px|em|rem|%)\s*$", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(part, @"^text-indent\s*:\s*[\d.]+(px|em|rem|%)\s*$", RegexOptions.IgnoreCase))
                        .ToList();

                    return allowed.Count == 0
                        ? string.Empty
                        : $" style=\"{string.Join("; ", allowed)}\"";
                },
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Keep only known safe alignment/indent classes used by rich text editors.
            cleaned = Regex.Replace(
                cleaned,
                @"\sclass\s*=\s*(['""])(.*?)\1",
                match =>
                {
                    var classValue = match.Groups[2].Value;
                    var allowed = classValue
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Where(c =>
                            Regex.IsMatch(c, @"^ql-align-(left|right|center|justify)$", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(c, @"^ql-indent-\d+$", RegexOptions.IgnoreCase))
                        .ToList();

                    return allowed.Count == 0
                        ? string.Empty
                        : $" class=\"{string.Join(" ", allowed)}\"";
                },
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            cleaned = Regex.Replace(cleaned, @"</?font\b[^>]*>", string.Empty, RegexOptions.IgnoreCase);

            return cleaned.Trim();
        }
    }
}
