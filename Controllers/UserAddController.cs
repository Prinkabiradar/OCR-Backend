using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Modals;
using OCR_BACKEND.Services;

namespace OCR_BACKEND.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserAddController : ControllerBase
    {
        private readonly IUserAddService _service;

        public UserAddController(IUserAddService service)
        {
            _service = service;
        }

        [HttpPost("insert-update")]
        public async Task<IActionResult> InsertUpdateUser([FromBody] UserRequest model)
        {
            var UserId = await _service.InsertUpdateUserAsync(model);

            if (UserId == 0)
                return BadRequest(new { message = "Failed to save user" });

            return Ok(new
            {
                message = model.UserId == 0 ? "User created successfully" : "User updated successfully",
                UserId
            });
        }
    }
}
