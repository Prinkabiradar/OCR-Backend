using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Services;

namespace OCR_BACKEND.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductivityReportController : ControllerBase
    {
        private readonly IProductivityReportService _reportService;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _environment;

        public ProductivityReportController(
            IProductivityReportService reportService,
            IConfiguration config,
            IWebHostEnvironment environment)
        {
            _reportService = reportService;
            _config = config;
            _environment = environment;
        }

        [HttpPost("send-now")]
        public async Task<IActionResult> SendNow([FromBody] SendProductivityReportRequest request)
        {
            var isEnabled = _environment.IsDevelopment() || _config.GetValue<bool>("ProductivityReport:AllowManualSend");
            if (!isEnabled)
                return NotFound();

            if (request.ToEmails == null || request.ToEmails.Length == 0)
                return BadRequest(new { message = "At least one ToEmails value is required." });

            var reportDate = request.ReportDate ?? DateOnly.FromDateTime(DateTime.Now);
            await _reportService.SendReportEmailAsync(
                reportDate,
                request.ToEmails,
                request.CcEmails ?? Array.Empty<string>(),
                HttpContext.RequestAborted);

            return Ok(new { message = "Productivity report sent successfully.", reportDate });
        }
    }

    public class SendProductivityReportRequest
    {
        public string[] ToEmails { get; set; } = Array.Empty<string>();
        public string[]? CcEmails { get; set; }
        public DateOnly? ReportDate { get; set; }
    }
}
