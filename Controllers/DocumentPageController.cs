using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Modals;
using OCR_BACKEND.Services;
using System.Data;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace OCR_BACKEND.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DocumentPageController : ControllerBase
    {
        private readonly IDocumentPageService _service;
        private readonly IConfiguration _config;

        public DocumentPageController(IDocumentPageService service, IConfiguration config)
        {
            _service = service;
            _config = config;
        }

        [HttpPost("InsertUpdateDocumentPage")]
        public async Task<IActionResult> InsertUpdateDocumentPage(DocumentPageRequest model)
        {
            var userClaims = HttpContext.User;
            var idClaim = userClaims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var RoleIdClaim = userClaims.FindFirst(ClaimTypes.Role)?.Value;
            if (!int.TryParse(idClaim, out int Id))
                return BadRequest("Invalid user ID.");
            if (!int.TryParse(RoleIdClaim, out int RoleId))
                return BadRequest("Invalid employee ID in token.");

            model.UserId = Id;
            model.RoleId = RoleId;

            var id = await _service.InsertUpdateDocumentPage(model);

            if (id == 0)
                return BadRequest(new { message = "Failed to save" });

            return Ok(new
            {
                message = model.DocumentPageId == 0 ? "Created Successfully" : "Updated Successfully",
                DocumentPageId = id
            });
        }

        [HttpGet("GetDocumentPages")]
        public async Task<IActionResult> GetDocumentPagesByDocument([FromQuery] OcrDocumentRequest request)
        {
            try
            {
                request.RoleId = 3;
                DataTable response = await _service.GetDocumentPagesByDocument(request);

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

        [HttpGet("GetDocumentById")]
        public async Task<IActionResult> GetDocumentsByDocumentType([FromQuery] DocumentFetchRequest pagination)
        {
            try
            {
                var userClaims = HttpContext.User;
                var idClaim = userClaims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var RoleIdClaim = userClaims.FindFirst(ClaimTypes.Role)?.Value;
                if (!int.TryParse(idClaim, out int Id))
                    return BadRequest("Invalid user ID.");
                if (!int.TryParse(RoleIdClaim, out int RoleId))
                    return BadRequest("Invalid employee ID in token.");

                pagination.RoleId = RoleId;
                DataTable response = await _service.GetDocumentsByDocumentType(pagination);

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

        [HttpGet("GetSuggestionPages")]
        public async Task<IActionResult> GetSuggestionPages([FromQuery] SuggestionPageRequest request)
        {
            try
            {
                var userClaims = HttpContext.User;
                var idClaim = userClaims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var RoleIdClaim = userClaims.FindFirst(ClaimTypes.Role)?.Value;
                if (!int.TryParse(idClaim, out int Id))
                    return BadRequest("Invalid user ID.");
                if (!int.TryParse(RoleIdClaim, out int RoleId))
                    return BadRequest("Invalid employee ID in token.");

                request.RoleId = RoleId;
                DataTable response = await _service.GetSuggestionPages(request);

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

        [HttpGet("GetDocumentFile")]
        public async Task<IActionResult> GetDocumentFile([FromQuery] int documentId, [FromQuery] int pageNumber = 1)
        {
            try
            {
                // ── 1. Get job_id from DB ─────────────────────────────────────────
                var request = new OcrDocumentRequest
                {
                    DocumentId = documentId,
                    StartIndex = pageNumber,
                    PageSize = 1,
                    SearchBy = null,
                    SearchCriteria = null,
                    RoleId = 0
                };

                DataTable dt = await _service.GetDocumentPagesByDocument(request);

                if (dt == null || dt.Rows.Count == 0)
                    return NotFound(new { message = "No pages found for this document." });

                var row = dt.Rows[0];
                var jobId = row["job_id"]?.ToString();

                if (string.IsNullOrWhiteSpace(jobId))
                    return NotFound(new { message = "Job ID not found for this document." });

                var storageRoot = _config["FileStorage:Root"] ?? "uploads";
                var originalsDir = Path.Combine(storageRoot, jobId, "originals");
                var convertedDir = Path.Combine(storageRoot, jobId, "converted");

                // ── 2. Find file matching page number in originals ────────────────
                string? filePath = null;

                if (Directory.Exists(originalsDir))
                {
                    var allOriginals = Directory.GetFiles(originalsDir)
                        .OrderBy(f => Path.GetFileName(f))
                        .ToList();

                    // If only one file uploaded (e.g. a PDF or single image), always return it
                    if (allOriginals.Count == 1)
                    {
                        filePath = allOriginals[0];
                    }
                    else
                    {
                        // Match by page number pattern e.g. _p1, _p2 or by index
                        filePath = allOriginals
                            .FirstOrDefault(f => Regex.IsMatch(
                                Path.GetFileNameWithoutExtension(f),
                                $@"_p{pageNumber}$", RegexOptions.IgnoreCase))
                            ?? allOriginals.ElementAtOrDefault(pageNumber - 1);
                    }
                }

                // ── 3. Fallback to converted folder ───────────────────────────────
                if (filePath == null && Directory.Exists(convertedDir))
                {
                    var allConverted = Directory.GetFiles(convertedDir)
                        .OrderBy(f => Path.GetFileName(f))
                        .ToList();

                    if (allConverted.Count == 1)
                    {
                        filePath = allConverted[0];
                    }
                    else
                    {
                        filePath = allConverted
                            .FirstOrDefault(f => Regex.IsMatch(
                                Path.GetFileNameWithoutExtension(f),
                                $@"_p{pageNumber}$", RegexOptions.IgnoreCase))
                            ?? allConverted.ElementAtOrDefault(pageNumber - 1);
                    }
                }

                if (filePath == null || !System.IO.File.Exists(filePath))
                    return NotFound(new { message = "No file found for this document." });

                // ── 4. Return as file stream (blob) ───────────────────────────────
                var contentType = GetContentType(filePath);
                var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
                return File(bytes, contentType);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private string GetContentType(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "application/pdf",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".tif" => "image/tiff",
                ".tiff" => "image/tiff",
                _ => "application/octet-stream"
            };
        }
    }
}