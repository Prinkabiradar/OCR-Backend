using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Modals;
using OCR_BACKEND.Services;

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
    }
}