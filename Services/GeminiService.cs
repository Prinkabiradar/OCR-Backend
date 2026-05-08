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
        public async Task<string> ExtractTextFromFileBytes(byte[] bytes, string contentType, string? modelOverride = null)
        {
            if (bytes.Length > MaxInlineBytes)
                throw new InvalidOperationException(
                    $"File size {bytes.Length / 1024 / 1024}MB exceeds the 18MB Gemini inline limit. " +
                    "Reduce Pdf:PagesPerChunk in appsettings.json.");

            var apiKey = ResolveApiKey();
            var model = string.IsNullOrWhiteSpace(modelOverride)
                ? (_config["Gemini:Model"] ?? "gemini-2.5-flash")
                : modelOverride.Trim();
            var base64 = Convert.ToBase64String(bytes);
            var isPdf = contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);

            // PDFs may be multi-page → ask for a JSON array, one element per page.
            // Images are always single-page → ask for a single JSON object.
                var prompt = isPdf
                        ? @"This is a PDF document. For EACH page return a JSON array where every element has exactly these fields:
 
                        [
                          {
                            ""page"": 1,
                            ""extracted_text"": ""<HTML content preserving headings, paragraphs, bold, italics, tables, and layout>"",
                            ""suggested_document_type"": ""<Letter|Poem|Novel|Book|Certificate|Invoice|Report|Legal|Article|Receipt|Form|Contract|Newspaper>"",
                            ""suggested_document_name"": ""<title or short descriptive name, max 10 words>""
                          }
                        ]
 
                        Rules:
                        - extracted_text MUST be valid clean HTML
                        - Use tags like <p>, <b>, <i>, <h1>, <table>, <tr>, <td>
                        - Preserve structure and layout as much as possible
                        - Maintain line breaks
                        - Try to reconstruct tables accurately
                        - NO markdown
                        - NO explanation
                        - Return ONLY JSON"

                        : @"Analyse this document image and return a JSON object with exactly these fields:
 
                        {
                          ""extracted_text"": ""<HTML content preserving headings, paragraphs, bold, italics, tables, and layout>"",
                          ""suggested_document_type"": ""<Letter|Poem|Novel|Book|Certificate|Invoice|Report|Legal|Article|Receipt|Form|Contract|Newspaper>"",
                          ""suggested_document_name"": ""<title or short descriptive name, max 10 words>""
                        }
 
                        Rules:
                        - extracted_text MUST be valid clean HTML
                        - Preserve layout as much as possible
                        - Use semantic HTML tags
                        - Keep tables intact
                        - NO markdown
                        - NO explanation
                        - Return ONLY JSON";

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
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                if (responseBody.Contains("API key not valid", StringComparison.OrdinalIgnoreCase) ||
                    responseBody.Contains("API Key not found", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Gemini API key is invalid or missing. Set a valid key in Gemini:ApiKey or GEMINI_API_KEY.");
                }

                throw new InvalidOperationException(
                    $"Gemini request failed ({(int)response.StatusCode}): {responseBody}");
            }

            return responseBody;
        }

        // ── Health check to verify Gemini API is available and responsive ────
        public async Task<(bool IsHealthy, string Message)> CheckGeminiHealth(string? modelOverride = null, CancellationToken ct = default)
        {
            try
            {
                var apiKey = ResolveApiKey();
                var model = string.IsNullOrWhiteSpace(modelOverride)
                    ? (_config["Gemini:Model"] ?? "gemini-2.5-flash")
                    : modelOverride.Trim();

                // Send a minimal test request to check API availability
                var testBody = new
                {
                    contents = new[]
                    {
                        new {
                            parts = new[]
                            {
                                new { text = "Respond with 'OK'" }
                            }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(testBody);
                var response = await _http.PostAsync(
                    $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}",
                    new StringContent(json, Encoding.UTF8, "application/json"),
                    ct);

                var responseBody = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    if ((int)response.StatusCode == 503)
                        return (false, "Gemini API is temporarily unavailable (503 Service Unavailable). Please try again later.");
                    
                    if ((int)response.StatusCode == 429)
                        return (false, "Gemini API is experiencing high demand (429 Too Many Requests). Please try again later.");
                    
                    if ((int)response.StatusCode == 500 || (int)response.StatusCode >= 502)
                        return (false, $"Gemini API server error ({(int)response.StatusCode}). Please try again later.");
                    
                    return (false, $"Gemini API health check failed: {responseBody}");
                }

                return (true, "Gemini API is healthy and ready to process documents.");
            }
            catch (HttpRequestException ex)
            {
                return (false, $"Cannot connect to Gemini API: {ex.Message}. Please check your internet connection.");
            }
            catch (TaskCanceledException)
            {
                return (false, "Gemini API health check timed out. Please try again.");
            }
            catch (Exception ex)
            {
                return (false, $"Unexpected error checking Gemini health: {ex.Message}");
            }
        }

        private string ResolveApiKey()
        {
            // Server deployments should be able to override file-based settings safely.
            var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                apiKey = _config["Gemini:ApiKey"];

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException(
                    "Gemini API key is not configured. Set Gemini:ApiKey in appsettings or GEMINI_API_KEY.");

            return apiKey.Trim();
        }

        // ── Keep old name as a thin wrapper so nothing else breaks ──────────
        [Obsolete("Use ExtractTextFromFileBytes instead")]
        public Task<string> ExtractTextFromImageBytes(byte[] bytes, string contentType)
            => ExtractTextFromFileBytes(bytes, contentType);
    }
}
