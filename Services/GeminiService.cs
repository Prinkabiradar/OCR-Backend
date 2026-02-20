using System.Text;
using System.Text.Json;

namespace OCR_BACKEND.Services
{
    public class GeminiService
    {
        private readonly IConfiguration _config;
        private readonly HttpClient _http;

        public GeminiService(IConfiguration config, HttpClient http)
        {
            _config = config;
            _http = http;
        }

        public async Task<string> ExtractTextFromImage(IFormFile file)
        {
            var apiKey = _config["Gemini:ApiKey"];

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var base64 = Convert.ToBase64String(bytes);

            var body = new
            {
                contents = new[]
                {
                new {
                    parts = new object[]
                    {
                        new { text = "Extract all text from this image (OCR)." },
                        new {
                            inline_data = new {
                                mime_type = file.ContentType,
                                data = base64
                            }
                        }
                    }
                }
            }
            };

            var json = JsonSerializer.Serialize(body);

            var response = await _http.PostAsync(
    $"https://generativelanguage.googleapis.com/v1/models/gemini-2.5-flash:generateContent?key={apiKey}",
    new StringContent(json, Encoding.UTF8, "application/json")
);


            return await response.Content.ReadAsStringAsync();
        }
    }
}
