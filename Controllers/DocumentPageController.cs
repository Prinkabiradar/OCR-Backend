using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Modals;
using OCR_BACKEND.Services;
using System.Data;
using System.Reflection;
using System.Security.Claims;

namespace OCR_BACKEND.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DocumentPageController : ControllerBase
    {
        private readonly IDocumentPageService _service;

        public DocumentPageController(IDocumentPageService service)
        {
            _service = service;
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
            {
                return BadRequest("Invalid employee ID in token.");
            }

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
                //var userClaims = HttpContext.User;
                //var idClaim = userClaims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                //var RoleIdClaim = userClaims.FindFirst(ClaimTypes.Role)?.Value;
                //if (!int.TryParse(idClaim, out int Id))
                //    return BadRequest("Invalid user ID.");
                //if (!int.TryParse(RoleIdClaim, out int RoleId))
                //{
                //    return BadRequest("Invalid employee ID in token.");
                //}

                // model.UserId = Id;
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
                {
                    return BadRequest("Invalid employee ID in token.");
                }

                // model.UserId = Id;
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
                {
                    return BadRequest("Invalid employee ID in token.");
                }

                // model.UserId = Id;
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
        public async Task<IActionResult> GetDocumentFile(int documentId)
        {
            var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");

            if (!Directory.Exists(uploadsPath))
                return NotFound("Uploads folder not found");

            // Find folder matching documentId
            var targetFolder = Path.Combine(uploadsPath, documentId.ToString());

            string? filePath = null;

            if (Directory.Exists(targetFolder))
            {
                // Look inside the specific document folder
                filePath = Directory.GetFiles(targetFolder).FirstOrDefault();
            }
            else
            {
                // Fallback: search all folders (current behavior)
                filePath = Directory.GetDirectories(uploadsPath)
                    .SelectMany(folder => Directory.GetFiles(folder))
                    .FirstOrDefault();
            }

            if (filePath == null || !System.IO.File.Exists(filePath))
                return NotFound("No file found for this document");

            var contentType = GetContentType(filePath);

            // ✅ Async file read
            var bytes = await System.IO.File.ReadAllBytesAsync(filePath);

            return File(bytes, contentType);
        }

        // ✅ ADD HELPER HERE (inside same class)
        private string GetContentType(string path)
        {
            var ext = Path.GetExtension(path).ToLower();

            return ext switch
            {
                ".pdf" => "application/pdf",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                _ => "application/octet-stream"
            };
        }
    }
}

