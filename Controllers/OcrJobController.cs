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
        private readonly IConfiguration _config;

        public OcrJobController(IOcrJobService service, IConfiguration config)
        {
            _service = service;
            _config = config;
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

                var modelCandidates = BuildModelCandidates(request.GeminiModel);
                var resolvedModel = await ResolveFirstHealthyModel(modelCandidates, ct);
                if (!resolvedModel.IsHealthy || string.IsNullOrWhiteSpace(resolvedModel.Model))
                {
                    return StatusCode(503, new 
                    { 
                        message = "Cannot process documents at this time",
                        details = resolvedModel.Message,
                        triedModels = modelCandidates
                    });
                }

                var jobId = await _service.UploadAndEnqueue(request.Files, resolvedModel.Model, ct);

                return Ok(new
                {
                    message = $"{request.Files.Count} files queued successfully",
                    JobId = jobId,
                    selectedModel = resolvedModel.Model,
                    fallbackUsed = !string.IsNullOrWhiteSpace(request.GeminiModel) &&
                                   !string.Equals(request.GeminiModel.Trim(), resolvedModel.Model, StringComparison.OrdinalIgnoreCase),
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

        [HttpPost("RetryResult")]
        public async Task<IActionResult> RetryResult(
            [FromBody] RetryOcrResultRequest request,
            CancellationToken ct)
        {
            try
            {
                if (request.JobId == Guid.Empty || string.IsNullOrWhiteSpace(request.FileName))
                    return BadRequest(new { message = "JobId and FileName are required." });

                var modelCandidates = BuildModelCandidates(request.GeminiModel);
                var resolvedModel = await ResolveFirstHealthyModel(modelCandidates, ct);
                if (!resolvedModel.IsHealthy || string.IsNullOrWhiteSpace(resolvedModel.Model))
                {
                    return StatusCode(503, new
                    {
                        message = "Cannot retry OCR at this time",
                        details = resolvedModel.Message,
                        triedModels = modelCandidates
                    });
                }

                var result = await _service.RetryResult(request.JobId, request.FileName, resolvedModel.Model, ct);
                return Ok(new
                {
                    result,
                    selectedModel = resolvedModel.Model,
                    fallbackUsed = !string.IsNullOrWhiteSpace(request.GeminiModel) &&
                                   !string.Equals(request.GeminiModel.Trim(), resolvedModel.Model, StringComparison.OrdinalIgnoreCase)
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("CancelJob")]
        public async Task<IActionResult> CancelJob(
            [FromBody] CancelOcrJobRequest request,
            CancellationToken ct)
        {
            try
            {
                if (request.JobId == Guid.Empty)
                    return BadRequest(new { message = "JobId is required." });

                await _service.CancelJob(request.JobId, ct);
                return Ok(new { message = "Job cancelled successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("VerifyPageIntegrity")]
        public async Task<IActionResult> VerifyPageIntegrity(
            [FromBody] VerifyOcrJobRequest request,
            CancellationToken ct)
        {
            try
            {
                if (request.JobId == Guid.Empty)
                    return BadRequest(new { message = "JobId is required." });

                var verification = await _service.VerifyPageIntegrity(request.JobId, request.ExpectedTotalPages, ct);
                return Ok(verification);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("CheckGeminiHealth")]
        public async Task<IActionResult> CheckGeminiHealth([FromQuery] string? model = null, CancellationToken ct = default)
        {
            try
            {
                var modelCandidates = BuildModelCandidates(model);
                var resolvedModel = await ResolveFirstHealthyModel(modelCandidates, ct);

                if (resolvedModel.IsHealthy && !string.IsNullOrWhiteSpace(resolvedModel.Model))
                {
                    return Ok(new 
                    { 
                        status = "healthy",
                        message = resolvedModel.Message,
                        selectedModel = resolvedModel.Model,
                        triedModels = modelCandidates,
                        canProcess = true
                    });
                }
                else
                {
                    return StatusCode(503, new 
                    { 
                        status = "unavailable",
                        message = resolvedModel.Message,
                        triedModels = modelCandidates,
                        canProcess = false
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(503, new 
                { 
                    status = "error",
                    message = $"Health check failed: {ex.Message}",
                    canProcess = false
                });
            }
        }

        private List<string> BuildModelCandidates(string? requestedModel)
        {
            var models = new List<string>();

            if (!string.IsNullOrWhiteSpace(requestedModel))
                models.Add(requestedModel.Trim());

            var primaryModel = (_config["Gemini:Model"] ?? "gemini-2.5-flash").Trim();
            if (!string.IsNullOrWhiteSpace(primaryModel))
                models.Add(primaryModel);

            var configuredFallbacks = _config.GetSection("Gemini:FallbackModels").Get<string[]>() ?? Array.Empty<string>();
            foreach (var fallback in configuredFallbacks)
            {
                if (!string.IsNullOrWhiteSpace(fallback))
                    models.Add(fallback.Trim());
            }

            // Safe defaults if no explicit fallback list is configured.
            // Keep this list aligned with currently supported models.
            models.Add("gemini-3.1-pro-preview");
            models.Add("gemini-3.1-pro-preview-customtools");
            models.Add("gemini-3-flash-preview");
            models.Add("gemini-3.1-flash-lite-preview");
            models.Add("gemini-2.5-flash");
            models.Add("gemini-2.5-pro");
            models.Add("gemini-2.0-flash");
            models.Add("gemini-1.5-flash");
            models.Add("gemini-1.5-pro");

            return models
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<(bool IsHealthy, string? Model, string Message)> ResolveFirstHealthyModel(
            IEnumerable<string> models,
            CancellationToken ct)
        {
            var errors = new List<string>();

            foreach (var model in models)
            {
                var (isHealthy, message) = await _service.CheckGeminiHealth(model, ct);
                if (isHealthy)
                    return (true, model, message);

                errors.Add($"{model}: {message}");
            }

            var combinedError = errors.Count == 0
                ? "No Gemini model candidates were available."
                : string.Join(" | ", errors);

            return (false, null, combinedError);
        }
    }
}
