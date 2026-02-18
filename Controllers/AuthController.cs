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

    }

    public class LoginModel
    {
        public string username { get; set; }
        public string password { get; set; }
    }

}
