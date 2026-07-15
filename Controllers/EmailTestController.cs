using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Services;

namespace OCR_BACKEND.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EmailTestController : ControllerBase
    {
        private readonly IEmailService _emailService;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _environment;

        public EmailTestController(
            IEmailService emailService,
            IConfiguration config,
            IWebHostEnvironment environment)
        {
            _emailService = emailService;
            _config = config;
            _environment = environment;
        }

        [HttpPost("send")]
        public async Task<IActionResult> Send([FromBody] TestEmailRequest request)
        {
            var isEnabled = _environment.IsDevelopment() || _config.GetValue<bool>("Email:AllowTestEndpoint");
            if (!isEnabled)
                return NotFound();

            if (string.IsNullOrWhiteSpace(request.ToEmail))
                return BadRequest(new { message = "ToEmail is required." });

            await _emailService.SendEmailAsync(
                request.ToEmail,
                "OCR App SMTP Test",
                $@"
                    <div style='font-family:Arial,sans-serif;max-width:520px;'>
                        <h2>SMTP test successful</h2>
                        <p>This email was sent by the OCR backend using the configured Sharpflux SMTP settings.</p>
                        <p><strong>Sent at:</strong> {DateTime.Now:dd MMM yyyy hh:mm tt}</p>
                    </div>");

            return Ok(new { message = "Test email sent successfully." });
        }
    }

    public class TestEmailRequest
    {
        public string ToEmail { get; set; } = string.Empty;
    }
}
