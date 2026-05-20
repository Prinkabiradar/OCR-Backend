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
        private readonly IStorageService _storage;
        private readonly ILogger<DocumentPageController> _logger;

        public DocumentPageController(
            IDocumentPageService service,
            IConfiguration config,
            IStorageService storage,
            ILogger<DocumentPageController> logger)
        {
            _service = service;
            _config = config;
            _storage = storage;
            _logger = logger;
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
                ////var userClaims = HttpContext.User;
                ////var idClaim = userClaims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                //var RoleIdClaim = userClaims.FindFirst(ClaimTypes.Role)?.Value;
                //if (!int.TryParse(idClaim, out int Id))
                //    return BadRequest("Invalid user ID.");
                //if (!int.TryParse(RoleIdClaim, out int RoleId))
                //    return BadRequest("Invalid employee ID in token.");

                //pagination.RoleId = 1;
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
        public async Task<IActionResult> GetDocumentFile(
            [FromQuery] int documentId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] string? filePath = null,
            [FromQuery] string? requestJobId = null)
        {
            try
            {
                _logger.LogInformation(
                    "GetDocumentFile request: documentId={DocumentId}, pageNumber={PageNumber}, requestJobId={RequestJobId}, filePath={FilePath}, storageType={StorageType}",
                    documentId, pageNumber, requestJobId, filePath, _storage.GetStorageType());

                // ── 0. Fast path: if caller provides exact file path, use it directly ──
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    var normalizedPath = filePath.Replace("\\", "/");
                    var pathParts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                    var directFileName = Path.GetFileName(normalizedPath);
                    var directFileType = normalizedPath.Contains("/originals/", StringComparison.OrdinalIgnoreCase)
                        ? "originals"
                        : normalizedPath.Contains("/converted/", StringComparison.OrdinalIgnoreCase)
                            ? "converted"
                            : (pathParts.Length >= 2 ? pathParts[0] : null);

                    var resolvedJobId = requestJobId;
                    if (string.IsNullOrWhiteSpace(resolvedJobId))
                    {
                        // Supports S3 key format: ocr-jobs/{jobId}/{type}/{file}
                        var ocrJobsIndex = Array.FindIndex(pathParts, p => p.Equals("ocr-jobs", StringComparison.OrdinalIgnoreCase));
                        if (ocrJobsIndex >= 0 && pathParts.Length > ocrJobsIndex + 1)
                            resolvedJobId = pathParts[ocrJobsIndex + 1];
                        else if (pathParts.Length >= 3)
                            // Supports local-relative format: {jobId}/{type}/{file}
                            resolvedJobId = pathParts[0];
                    }

                    if (!string.IsNullOrWhiteSpace(resolvedJobId) &&
                        !string.IsNullOrWhiteSpace(directFileType) &&
                        !string.IsNullOrWhiteSpace(directFileName))
                    {
                        _logger.LogInformation(
                            "GetDocumentFile direct lookup: jobId={JobId}, fileType={FileType}, fileName={FileName}",
                            resolvedJobId, directFileType, directFileName);
                        var directBytes = await _storage.GetFileAsyncBytes(resolvedJobId, directFileType, directFileName);
                        if (directBytes != null && directBytes.Length > 0)
                        {
                            _logger.LogInformation(
                                "GetDocumentFile direct lookup success: bytes={ByteCount}",
                                directBytes.Length);
                            return File(directBytes, GetContentType(directFileName));
                        }

                        _logger.LogWarning(
                            "GetDocumentFile direct lookup returned empty: jobId={JobId}, fileType={FileType}, fileName={FileName}",
                            resolvedJobId, directFileType, directFileName);
                    }
                }

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
                var dbFilePath = row["filepath"]?.ToString() ?? row["FilePath"]?.ToString();
                _logger.LogInformation(
                    "GetDocumentFile DB row: jobId={JobId}, dbFilePath={DbFilePath}",
                    jobId, dbFilePath);

                if (string.IsNullOrWhiteSpace(jobId))
                    return NotFound(new { message = "Job ID not found for this document." });

                string? fileType = null;
                string? fileName = null;

                if (!string.IsNullOrWhiteSpace(dbFilePath))
                {
                    var normalizedPath = dbFilePath.Replace("\\", "/");
                    fileName = Path.GetFileName(normalizedPath);

                    // Supports both:
                    // 1) originals/file.pdf
                    // 2) ocr-jobs/{jobId}/originals/file.pdf (DigitalOcean key)
                    if (normalizedPath.Contains("/originals/", StringComparison.OrdinalIgnoreCase))
                    {
                        fileType = "originals";
                    }
                    else if (normalizedPath.Contains("/converted/", StringComparison.OrdinalIgnoreCase))
                    {
                        fileType = "converted";
                    }
                    else
                    {
                        var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                            fileType = parts[0];
                    }
                }

                if (string.IsNullOrWhiteSpace(fileType) || string.IsNullOrWhiteSpace(fileName))
                {
                    var originals = await _storage.ListFilesAsync(jobId, "originals");
                    _logger.LogInformation(
                        "GetDocumentFile originals list count: {Count} for jobId={JobId}",
                        originals.Count, jobId);
                    if (originals.Count > 0)
                    {
                        fileType = "originals";
                        fileName = originals
                            .FirstOrDefault(f => Regex.IsMatch(
                                Path.GetFileNameWithoutExtension(f),
                                $@"_p{pageNumber}$", RegexOptions.IgnoreCase))
                            ?? originals.ElementAtOrDefault(pageNumber - 1)
                            ?? originals.FirstOrDefault();
                    }
                }

                if ((string.IsNullOrWhiteSpace(fileType) || string.IsNullOrWhiteSpace(fileName)))
                {
                    var converted = await _storage.ListFilesAsync(jobId, "converted");
                    _logger.LogInformation(
                        "GetDocumentFile converted list count: {Count} for jobId={JobId}",
                        converted.Count, jobId);
                    if (converted.Count > 0)
                    {
                        fileType = "converted";
                        fileName = converted
                            .FirstOrDefault(f => Regex.IsMatch(
                                Path.GetFileNameWithoutExtension(f),
                                $@"_p{pageNumber}$", RegexOptions.IgnoreCase))
                            ?? converted.ElementAtOrDefault(pageNumber - 1)
                            ?? converted.FirstOrDefault();
                    }
                }

                if (string.IsNullOrWhiteSpace(fileType) || string.IsNullOrWhiteSpace(fileName))
                {
                    _logger.LogWarning(
                        "GetDocumentFile file resolution failed: documentId={DocumentId}, pageNumber={PageNumber}, jobId={JobId}",
                        documentId, pageNumber, jobId);
                    return NotFound(new { message = "No file found for this document." });
                }

                _logger.LogInformation(
                    "GetDocumentFile final lookup: jobId={JobId}, fileType={FileType}, fileName={FileName}",
                    jobId, fileType, fileName);
                var bytes = await _storage.GetFileAsyncBytes(jobId, fileType, fileName);
                if (bytes == null || bytes.Length == 0)
                {
                    _logger.LogWarning(
                        "GetDocumentFile storage bytes empty: jobId={JobId}, fileType={FileType}, fileName={FileName}",
                        jobId, fileType, fileName);
                    return NotFound(new { message = "No file found for this document in storage." });
                }

                var contentType = GetContentType(fileName);
                _logger.LogInformation(
                    "GetDocumentFile success: jobId={JobId}, fileName={FileName}, contentType={ContentType}, bytes={ByteCount}",
                    jobId, fileName, contentType, bytes.Length);
                return File(bytes, contentType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "GetDocumentFile exception: documentId={DocumentId}, pageNumber={PageNumber}, requestJobId={RequestJobId}, filePath={FilePath}",
                    documentId, pageNumber, requestJobId, filePath);
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
