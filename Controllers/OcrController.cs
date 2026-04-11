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

            // Read all file bytes synchronously BEFORE parallelizing
            var fileData = new List<(string FileName, string ContentType, byte[] Bytes)>();
            foreach (var file in files)
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                fileData.Add((file.FileName, file.ContentType, ms.ToArray()));
            }

            // Now process all in parallel safely
            var tasks = fileData.Select(async f =>
            {
                var text = await _gemini.ExtractTextFromFileBytes(f.Bytes, f.ContentType);
                return new { FileName = f.FileName, OcrResult = text };
            });

            var results = await Task.WhenAll(tasks);
            return Ok(results);
        }
        //[HttpPost("image")]
        //public async Task<IActionResult> Upload(List<IFormFile> files)
        //{
        //    if (files == null || files.Count == 0)
        //        return BadRequest("No files uploaded");

        //    var results = new List<object>();

        //    foreach (var file in files)
        //    {
        //        var text = await _gemini.ExtractTextFromImage(file);

        //        results.Add(new
        //        {
        //            FileName = file.FileName,
        //            OcrResult = text
        //        });
        //    }

        //    return Ok(results);
        //}

    }
}
