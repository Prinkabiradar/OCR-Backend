using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Modals;
using OCR_BACKEND.Services;

namespace OCR_BACKEND.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TranslationController : ControllerBase
    {
        private readonly GeminiService _gemini;

        public TranslationController(GeminiService gemini) => _gemini = gemini;

        [HttpPost("ocr")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> TranslateOcr([FromForm] TranslationRequest request, CancellationToken ct)
        {
            if (request.Files.Count == 0)
                return BadRequest(new { message = "At least one document file is required." });
            if (string.IsNullOrWhiteSpace(request.TargetLanguage))
                return BadRequest(new { message = "TargetLanguage is required." });

            var results = new List<object>();
            foreach (var file in request.Files)
            {
                if (file.Length == 0) continue;
                await using var stream = new MemoryStream();
                await file.CopyToAsync(stream, ct);
                var contentType = string.IsNullOrWhiteSpace(file.ContentType)
                    ? "application/octet-stream"
                    : file.ContentType;
                var translation = await _gemini.ExtractAndTranslateFileBytes(
                    stream.ToArray(), contentType, request.TargetLanguage,
                    request.SourceLanguage, request.GeminiModel, ct);
                results.Add(new { fileName = file.FileName, translationResult = translation });
            }

            return Ok(results);
        }
    }
}
