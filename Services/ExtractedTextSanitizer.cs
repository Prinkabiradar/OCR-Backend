using System.Net;
using System.Text.RegularExpressions;

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

            // Remove style/class attributes and legacy font tags so text renders in app default (black).
            cleaned = Regex.Replace(cleaned, @"\sstyle\s*=\s*(['""]).*?\1", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            cleaned = Regex.Replace(cleaned, @"\sclass\s*=\s*(['""]).*?\1", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            cleaned = Regex.Replace(cleaned, @"</?font\b[^>]*>", string.Empty, RegexOptions.IgnoreCase);

            return cleaned.Trim();
        }
    }
}
