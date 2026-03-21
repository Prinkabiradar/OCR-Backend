using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Services;

namespace OCR_BACKEND.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly IDashboardService _service;

        public DashboardController(IDashboardService service)
        {
            _service = service;
        }

        // GET api/Dashboard/GetFullDashboard
        [HttpGet("GetFullDashboard")]
        public async Task<IActionResult> GetFullDashboard()
        {
            try
            {
                var response = await _service.GetFullDashboard();
                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}