using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Modals;
using OCR_BACKEND.Services;
using System.Data;
using System.Reflection;

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
        //[HttpPost("summarize")]
        //public async Task<IActionResult> Summarize([FromBody] AgentRequest request)
        //{
        //    if (string.IsNullOrWhiteSpace(request.Question))
        //        return BadRequest(new { message = "Document name is required." });

        //    var summary = await _service.Summarize(request.Question);
        //    return Ok(new { summary });
        //}

        [HttpPost("summarize")]
        public async Task<IActionResult> Summarize([FromBody] AgentRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Question))
                    return BadRequest(new { message = "Document name is required." });

                var summary = await _service.Summarize(request.Question);
                return Ok(new { summary });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });  
            }
        }

        [HttpPost("save-summary")]
        public async Task<IActionResult> SaveSummary([FromBody] SaveSummaryRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.DocumentName) ||
                string.IsNullOrWhiteSpace(request.SummaryText))
                return BadRequest(new { message = "DocumentName and SummaryText are required." });

            var result = await _service.SaveSummary(
                request.DocumentName,
                request.SummaryText,
                request.SummaryId,
                request.UserId,
                request.RoleId);       

            return Ok(result);
        }

        [HttpGet("GetSummaryData")]
        public async Task<IActionResult> GetSummaryData([FromQuery] SummaryData model)
        {
            try
            {
                DataTable response = await _service.GetSummaryData(model);

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