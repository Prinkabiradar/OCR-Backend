using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Modals;
using OCR_BACKEND.Services;

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
            var id = await _service.InsertUpdateDocument(model);

            if (id == 0)
                return BadRequest(new { message = "Failed to save" });

            return Ok(new
            {
                message = model.DocumentId == 0 ? "Created Successfully" : "Updated Successfully",
                DocumentId = id
            });
        }
    }
}
