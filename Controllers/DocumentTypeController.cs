using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Modals;
using OCR_BACKEND.Services;
using System.Data;

namespace OCR_BACKEND.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DocumentTypeController : ControllerBase
    {
        private readonly IDocumentTypeService _service;

        public DocumentTypeController(IDocumentTypeService service)
        {
            _service = service;
        }

        [HttpPost("InsertUpdateDocumentType")]
        public async Task<IActionResult> InsertUpdateDocumentType(DocumentTypeRequest model)
        {
            var id = await _service.InsertUpdateDocumentType(model);

            if (id == 0)
                return BadRequest(new { message = "Failed to save" });

            return Ok(new
            {
                message = model.DocumentTypeId == 0 ? "Created Successfully" : "Updated Successfully",
                DocumentTypeId = id
            });
        }

        [HttpGet("GetDocumentType")]
        public async Task<IActionResult> GetDocumentType([FromQuery] DocumentFetchRequest pagination)
        {
            try
            {
                DataTable response = await _service.GetDocumentType(pagination);

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