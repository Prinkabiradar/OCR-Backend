using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Modals;
using OCR_BACKEND.Services;
using System.Data;

namespace OCR_BACKEND.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OcrJobController : ControllerBase
    {
        private readonly IOcrJobService _service;

        public OcrJobController(IOcrJobService service)
        {
            _service = service;
        }

        [HttpPost("UploadImages")]
        [RequestSizeLimit(500 * 1024 * 1024)]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadImages(
            [FromForm] OcrUploadRequest request, CancellationToken ct)
        {
            try
            {
                if (request.Files == null || request.Files.Count == 0)
                    return BadRequest(new { message = "No files uploaded" });

                var jobId = await _service.UploadAndEnqueue(request.Files, ct);

                return Ok(new
                {
                    message = $"{request.Files.Count} files queued successfully",
                    JobId = jobId,
                    StatusUrl = $"/api/OcrJob/GetOcrJobById?jobId={jobId}"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("GetOcrJobs")]
        public async Task<IActionResult> GetOcrJobs([FromQuery] OcrJobFetchRequest pagination)
        {
            try
            {
                DataTable response = await _service.GetOcrJobs(pagination);
                var lst = response.AsEnumerable()
                    .Select(r => r.Table.Columns.Cast<DataColumn>()
                        .Select(c => new KeyValuePair<string, object>(c.ColumnName, r[c.Ordinal]))
                        .ToDictionary(z => z.Key, z => z.Value)
                    ).ToList();

                return Ok(lst);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("GetOcrJobById")]
        public async Task<IActionResult> GetOcrJobById([FromQuery] Guid jobId)
        {
            try
            {
                DataTable response = await _service.GetOcrJobById(jobId);
                var lst = response.AsEnumerable()
                    .Select(r => r.Table.Columns.Cast<DataColumn>()
                        .Select(c => new KeyValuePair<string, object>(c.ColumnName, r[c.Ordinal]))
                        .ToDictionary(z => z.Key, z => z.Value)
                    ).ToList();

                if (lst.Count == 0)
                    return NotFound(new { message = "Job not found" });

                return Ok(lst[0]);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("GetOcrJobResults")]
        public async Task<IActionResult> GetOcrJobResults([FromQuery] Guid jobId)
        {
            try
            {
                DataTable response = await _service.GetOcrJobResults(jobId);
                var lst = response.AsEnumerable()
                    .Select(r => r.Table.Columns.Cast<DataColumn>()
                        .Select(c => new KeyValuePair<string, object>(c.ColumnName, r[c.Ordinal]))
                        .ToDictionary(z => z.Key, z => z.Value)
                    ).ToList();

                return Ok(lst);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
