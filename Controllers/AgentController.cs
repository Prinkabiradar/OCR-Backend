using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Modals;
using OCR_BACKEND.Services;

namespace OCR_BACKEND.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AgentController : ControllerBase
    {
        private readonly IAgentService _service;
        public AgentController(IAgentService service)
        {
            _service = service;
        }

        [HttpGet("AgentGET")]
        public async Task<IActionResult> AgentGET(
       [FromQuery] string startIndex,
       [FromQuery] string pageSize,
       [FromQuery] string searchBy,
       [FromQuery] string? searchCriteria)
        {
       
            var question = searchCriteria ?? string.Empty;

            if (string.IsNullOrWhiteSpace(question))
                return BadRequest(new { message = "SearchCriteria (question) is required." });

            var result = await _service.Ask(
                question,
                int.Parse(startIndex),
                int.Parse(pageSize)
            );

            return Ok(result);
        }
        [HttpPost("summarize")]
        public async Task<IActionResult> Summarize([FromBody] AgentRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Question))
                return BadRequest(new { message = "Document name is required." });

            var summary = await _service.Summarize(request.Question);
            return Ok(new { summary });
        }
    }
}