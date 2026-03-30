using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Modals;
using OCR_BACKEND.Services;
using System.Data;

namespace OCR_BACKEND.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SuggestionController : ControllerBase
    {
        private readonly ISuggestionService _service;

        public SuggestionController(ISuggestionService service)
        {
            _service = service;
        }

        // ✅ INSERT SUGGESTION
        [HttpPost("insert")]
        public async Task<IActionResult> InsertSuggestion([FromBody] SuggestionRequest model)
        {
            var result = await _service.InsertPageSuggestion(model);
            return Ok(result);
        }

        // ✅ GET ACTIVE SUGGESTION
        [HttpGet("GetActiveSuggestion")]
        public async Task<IActionResult> GetActiveSuggestion([FromQuery] DocumentFetchRequest request)
        {
            var dt = await _service.GetActiveSuggestion(request);

            var lst = dt.AsEnumerable()
                    .Select(r => r.Table.Columns.Cast<DataColumn>()
                        .Select(c => new KeyValuePair<string, object>(c.ColumnName, r[c.Ordinal]))
                        .ToDictionary(z => z.Key, z => z.Value)
                    ).ToList();

            return Ok(lst);
        }

        // ✅ ACCEPT / REJECT
        [HttpPost("review")]
        public async Task<IActionResult> ReviewSuggestion([FromBody] ReviewSuggestionRequest model)
        {
            var result = await _service.ReviewSuggestion(
                model.SuggestionId,
                model.DocumentPageId,
                model.Action,
                model.ReviewedBy,
                model.RoleId
            );

            return Ok(result);
        }
    }
}