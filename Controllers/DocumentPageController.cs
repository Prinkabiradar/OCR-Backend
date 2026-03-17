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
    }
}

