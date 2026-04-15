using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Modals;
using OCR_BACKEND.Services;
using System.Data;

namespace OCR_BACKEND.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RoleAccessController : ControllerBase
    {
        private readonly IRoleAccessService _roleAccessService;
        public RoleAccessController(IRoleAccessService service)
        {
            _roleAccessService = service;
        }
          
        [HttpPost("InsertUpdateRoleAccess")]
        public async Task<IActionResult> InserUpdateRoleAccess(RoleMenuAccess model)
        {
            var id = await _roleAccessService.InserUpdateRoleAccess(model);

            if (id == 0)
                return BadRequest(new { message = "Failed to save" });

            return Ok(new
            {
                message = model.RoleAccessId == 0 ? "Created Successfully" : "Updated Successfully",
                RoleAccessId = id
            });
        }

        [HttpPost("InsertUpdateRole")]
        public async Task<IActionResult> InserUpdateRole(RoleModel model)
        {
            var id = await _roleAccessService.InserUpdateRole(model);

            if (id == 0)
                return BadRequest(new { message = "Failed to save" });

            return Ok(new
            {
                message = model.RoleId == 0 ? "Created Successfully" : "Updated Successfully",
                RoleId = id
            });
        }
        [HttpGet("GetRoles")]
        public async Task<IActionResult> GetRoles([FromQuery] DocRequest pagination)
        {
            try
            {
                DataTable response = await _roleAccessService.GetRoles(pagination);

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
