using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Modals;
using OCR_BACKEND.Services;

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
            var id = await _service.InsertUpdateDocumentPage(model);

            if (id == 0)
                return BadRequest(new { message = "Failed to save" });

            return Ok(new
            {
                message = model.DocumentPageId == 0 ? "Created Successfully" : "Updated Successfully",
                DocumentPageId = id
            });
        }
    }
}
