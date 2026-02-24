using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Modals;
using OCR_BACKEND.Services;
using System.Data;
using System.Security.Claims;

namespace OCR_BACKEND.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MenuController : ControllerBase
    {

        private readonly IMenuService _menuService;

        public MenuController(IMenuService menuService)
        {
            _menuService = menuService;
        }


        [Authorize]
        [HttpGet("getmenu")]
        public async Task<ActionResult<List<MenuItem>>> GetMenuByRole()
        {
            try
            {
                // Retrieve claims from the authorized user's token
                var userClaims = HttpContext.User;

                var idClaim = userClaims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var username = userClaims.FindFirst(ClaimTypes.Name)?.Value;
                var role = userClaims.FindFirst(ClaimTypes.Role)?.Value;

                // Validate the claims
                if (string.IsNullOrEmpty(idClaim) || string.IsNullOrEmpty(role))
                {
                    return Unauthorized("User claims are missing or invalid.");
                }

                int roleId;
                if (!int.TryParse(role, out roleId))
                {
                    return BadRequest("Invalid role ID in token.");
                }

                // Call your service method to fetch menu items for the specific role ID
                var menuItems = await _menuService.GetMenuByRole(roleId);

                if (menuItems == null || menuItems.Count == 0)
                {
                    return NotFound("No menu items found for the specified role.");
                }

                return Ok(menuItems);
            }
            catch (Exception ex)
            {
                // Log the exception (consider using a logging framework)
                Console.WriteLine($"Error in GetMenuByRole: {ex.Message}");
                return StatusCode(500, "Internal server error. Please try again later.");
            }
        }


        [Authorize]
        [HttpGet("getmenu2")]
        public async Task<ActionResult<List<MenuItem>>> GetMenuByRole2(int roleId)
        {
            try
            {
                var menuItems = await _menuService.GetMenuByRole(roleId);
                if (menuItems == null || menuItems.Count == 0)
                {
                    return NotFound("No menu items found for the specified role.");
                }
                return Ok(menuItems);
            }
            catch (System.Exception ex)
            {
                // Log the exception (consider using a logging framework)
                return StatusCode(500, "Internal server error. Please try again later.");
            }
        }
        //[HttpGet("SideMenuGetReports")]
        //public async Task<IActionResult> SideMenuGetReports(string startIndex)
        //{
        //    try
        //    {
        //        PaginationRequest model = new PaginationRequest();
        //        model.StartIndex = startIndex;


        //        DataTable response = await _menuService.SideMenuGetReports(model);

        //        var lst = response.AsEnumerable()
        //               .Select(r => r.Table.Columns.Cast<DataColumn>()
        //               .Select(c => new KeyValuePair<string, object>(c.ColumnName, r[c.Ordinal])
        //            ).ToDictionary(z => z.Key, z => z.Value)
        //         ).ToList();

        //        return Ok(lst);
        //    }
        //    catch (System.Exception ex)
        //    {

        //        return BadRequest(new { message = ex.Message });
        //    }
        //}
    }
}

