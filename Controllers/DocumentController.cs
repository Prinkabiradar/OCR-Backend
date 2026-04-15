using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Modals;
using OCR_BACKEND.Services;
using System.Data;

namespace OCR_BACKEND.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DocumentController : ControllerBase
    {
        private readonly IDocumentService _service;

        public DocumentController(IDocumentService service)
        {
            _service = service;
        }

        [HttpPost("InsertUpdateDocument")]
        public async Task<IActionResult> InsertUpdateDocument(DocumentRequest model)
        {
            try
            {
                var id = await _service.InsertUpdateDocument(model);
                if (id == 0)
                    return BadRequest(new { message = "Failed to save" });

                return Ok(new
                {
                    message = model.DocumentId == 0 ? "Created Successfully" : "Updated Successfully",
                    DocumentId = id
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "P0001")
            {
                return BadRequest(new { message = ex.MessageText });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("GetDocuments")]
        public async Task<IActionResult> GetDocuments([FromQuery] DocumentFetchRequest pagination)
        {
            try
            {
                DataTable response = await _service.GetDocuments(pagination);

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
        [HttpPost("ManageLock")]
        public async Task<IActionResult> ManageLock(ManageLockRequest model)
        {
            try
            {
                var result = await _service.ManageDocumentLock(model);

                if (!result && model.Action.ToUpper() == "LOCK")
                {
                    return BadRequest(new
                    {
                        message = "This Document is under review by another user"
                    });
                }

                return Ok(new
                {
                    success = true
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
