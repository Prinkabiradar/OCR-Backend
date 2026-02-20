using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Services;

namespace OCR_BACKEND.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OcrController : ControllerBase
    {
        private readonly GeminiService _gemini;

        public OcrController(GeminiService gemini)
        {
            _gemini = gemini;
        }

        [HttpPost("image")]
        public async Task<IActionResult> Upload(List<IFormFile> files)
        {
            if (files == null || files.Count == 0)
                return BadRequest("No files uploaded");

            var results = new List<object>();

            foreach (var file in files)
            {
                var text = await _gemini.ExtractTextFromImage(file);

                results.Add(new
                {
                    FileName = file.FileName,
                    OcrResult = text
                });
            }

            return Ok(results);
        }

    }
}
