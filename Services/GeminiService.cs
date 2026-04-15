using System;
using System.Text;
using System.Text.Json;

namespace OCR_BACKEND.Services
{
    public class GeminiService
    {
        private readonly IConfiguration _config;
        private readonly HttpClient _http;

        // Gemini inline base64 limit is ~20MB — stay safe at 18MB
        private const long MaxInlineBytes = 18 * 1024 * 1024;

        public GeminiService(IConfiguration config, HttpClient http)
        {
            _config = config;
            _http = http;
        }

        // ── Single unified entry point for both images and PDFs ──────────────
        public async Task<string> ExtractTextFromFileBytes(byte[] bytes, string contentType)
        {
            if (bytes.Length > MaxInlineBytes)
                throw new InvalidOperationException(
                    $"File size {bytes.Length / 1024 / 1024}MB exceeds the 18MB Gemini inline limit. " +
                    "Reduce Pdf:PagesPerChunk in appsettings.json.");

            var apiKey = _config["Gemini:ApiKey"];
            var model = _config["Gemini:Model"] ?? "gemini-2.5-flash";
            var base64 = Convert.ToBase64String(bytes);
            var isPdf = contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);

            // PDFs may be multi-page → ask for a JSON array, one element per page.
            // Images are always single-page → ask for a single JSON object.
            var prompt = isPdf
                ? @"This is a PDF document. For EACH page return a JSON array where every
                  element has exactly these fields:
                  [{ ""page"": 1,
                     ""extracted_text"": ""<all text on that page>"",
                     ""suggested_document_type"": ""<Letter|Poem|Novel|Book|Certificate|Invoice|Report|Legal|Article|Receipt|Form|Contract|Newspaper>"",
                     ""suggested_document_name"": ""<title or short descriptive name, max 10 words>"" }]
                  Return ONLY the JSON array. No markdown, no explanation, no code fences."
                : @"Analyse this document image and return a JSON object with exactly these fields:
                  { ""extracted_text"": ""<all text extracted from the document>"",
                    ""suggested_document_type"": ""<Letter|Poem|Novel|Book|Certificate|Invoice|Report|Legal|Article|Receipt|Form|Contract|Newspaper>"",
                    ""suggested_document_name"": ""<title or short descriptive name, max 10 words>"" }
                  Return ONLY the JSON. No markdown, no explanation, no code fences.";

            var body = new
            {
                contents = new[]
                {
                    new {
                        parts = new object[]
                        {
                            new { inline_data = new { mime_type = contentType, data = base64 } },
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json"
                }
            };

            var json = JsonSerializer.Serialize(body);

            // ✅ Correct model name — gemini-2.0-flash exists and is fast
            var response = await _http.PostAsync(
      $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}",
      new StringContent(json, Encoding.UTF8, "application/json")
  );

            return await response.Content.ReadAsStringAsync();
        }

        // ── Keep old name as a thin wrapper so nothing else breaks ──────────
        [Obsolete("Use ExtractTextFromFileBytes instead")]
        public Task<string> ExtractTextFromImageBytes(byte[] bytes, string contentType)
            => ExtractTextFromFileBytes(bytes, contentType);
    }
}
