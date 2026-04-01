using System;
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
        public async Task<string> ExtractTextFromImageBytes(byte[] bytes, string contentType)
        {
            var apiKey = _config["Gemini:ApiKey"];
            var base64 = Convert.ToBase64String(bytes);

            var prompt = @"Analyse this document image and return a JSON object with exactly these three fields:
            {
              ""extracted_text"": ""<all text extracted from the document via OCR>"",
              ""suggested_document_type"": ""<one of: Letter, Poem, Novel, Book, Certificate, Invoice, Report, Legal, Article, Receipt, Form, Contract, Newspaper, or the best match you can infer>"",
              ""suggested_document_name"": ""<the document's own title or heading if visible, otherwise a short descriptive name based on its content — max 10 words>""
            }
            Return ONLY the JSON. No markdown, no explanation, no code fences.";

            var body = new
            {
                contents = new[]
                {
            new {
                parts = new object[]
                {
                    new { text = prompt },
                    new { inline_data = new { mime_type = contentType, data = base64 } }
                }
            }
        }
            };

            var json = JsonSerializer.Serialize(body);
            var response = await _http.PostAsync(
                $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}",
               // $"https://generativelanguage.googleapis.com/v1/models/gemini-1.5-pro:generateContent?key={apiKey}",
            new StringContent(json, Encoding.UTF8, "application/json")
            );
            return await response.Content.ReadAsStringAsync();
        }


        //        public async Task<string> ExtractTextFromImage(IFormFile file)
        //        {
        //            var apiKey = _config["Gemini:ApiKey"];

        //            using var ms = new MemoryStream();
        //            await file.CopyToAsync(ms);
        //            var bytes = ms.ToArray();
        //            var base64 = Convert.ToBase64String(bytes);

        //            var body = new
        //            {
        //                contents = new[]
        //                {
        //                new {
        //                    parts = new object[]
        //                    {
        //                        new { text = "Extract all text from this image (OCR)." },
        //                        new {
        //                            inline_data = new {
        //                                mime_type = file.ContentType,
        //                                data = base64
        //                            }
        //                        }
        //                    }
        //                }
        //            }
        //            };

        //            var json = JsonSerializer.Serialize(body);

        //            var response = await _http.PostAsync(
        //    $"https://generativelanguage.googleapis.com/v1/models/gemini-2.5-flash:generateContent?key={apiKey}",
        //    new StringContent(json, Encoding.UTF8, "application/json")
        //);


        //            return await response.Content.ReadAsStringAsync();
        //        }
    }
}
