using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Modals;
using OCR_BACKEND.Services;
using System.Data;

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
        [HttpGet("UsersGET")]
            public async Task<IActionResult> UsersGET(
                    int userId,
                    string startIndex,
                    string pageSize,
                    string searchBy,
                    string searchCriteria)
            {
                try
                {
                    PaginationRequest model = new PaginationRequest();
                    model.UserId = userId;
                    model.StartIndex = startIndex;
                    model.PageSize = pageSize;
                    model.SearchBy = searchBy;
                    model.SearchCriteria = searchCriteria;

                    DataTable response = await _service.UsersGET(model);

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
