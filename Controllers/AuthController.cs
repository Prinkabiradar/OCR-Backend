using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Services;
using OCR_BACKEND.Modals;


namespace OCR_BACKEND.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly JwtHelper _jwt;

        public AuthController(IUserService userService, JwtHelper jwt)
        {
            _userService = userService;
            _jwt = jwt;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginModel model)
        {
            var user = await _userService.AuthenticateUserAsync(
                model.username,
                model.password
            );
            if (user == null)
                return BadRequest(new { message = "Invalid credentials" });

            var token = _jwt.GenerateToken(user);
            return Ok(new AuthenticateResponse(user, token));
        }

        [HttpGet("getUserByAccessToken")]
        public async Task<IActionResult> GetUserByAccessToken(string AccessToken)
        {
            var response = await _userService.GetUserByAccessToken(AccessToken);

            if (response == null)
                return BadRequest(new { message = "Token is Invalid" });

            return Ok(response);
        }
        // Add to AuthController.cs

        [HttpPost("send-otp")]
        public async Task<IActionResult> SendOtp([FromBody] SendOtpModel model)
        {
            if (string.IsNullOrWhiteSpace(model.EmailOrMobile))
                return BadRequest(new { message = "Email or mobile is required." });

            var (success, message) = await _userService.SendOtpAsync(model.EmailOrMobile);

            if (!success)
                return BadRequest(new { message });   // ← return 400 so Angular error block fires

            return Ok(new { success, message });
        }

        [HttpPost("verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpModel model)
        {
            var (success, userId) = await _userService.VerifyOtpAsync(model.EmailOrMobile, model.Otp);
            if (!success)
                return BadRequest(new { message = "Invalid or expired OTP." });

            return Ok(new { success = true, userId });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordModel model)
        {
            if (model.NewPassword != model.ConfirmPassword)
                return BadRequest(new { message = "Passwords do not match." });

            var result = await _userService.ResetPasswordAsync(model.UserId, model.NewPassword);
            return result
                ? Ok(new { message = "Password reset successful." })
                : BadRequest(new { message = "Failed to reset password." });
        }

        // Register in Program.cs:
        // builder.Services.AddScoped<IEmailService, EmailService>();

        // Models
   

    }

    public class LoginModel
    {
        public string username { get; set; }
        public string password { get; set; }
    }
    public class SendOtpModel { public string EmailOrMobile { get; set; } }
    public class VerifyOtpModel { public string EmailOrMobile { get; set; } public string Otp { get; set; } }
    public class ResetPasswordModel
    {
        public int UserId { get; set; }
        public string NewPassword { get; set; }
        public string ConfirmPassword { get; set; }
    }
}
