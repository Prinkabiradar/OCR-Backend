using Microsoft.AspNetCore.Http;

namespace OCR_BACKEND.Modals
{
    public class TranslationRequest
    {
        public List<IFormFile> Files { get; set; } = new();
        public string TargetLanguage { get; set; } = string.Empty;
        public string? SourceLanguage { get; set; }
        public string? GeminiModel { get; set; }
    }
}
