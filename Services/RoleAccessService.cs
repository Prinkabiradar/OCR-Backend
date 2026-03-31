using OCR_BACKEND.Modals;
using System.Data;

namespace OCR_BACKEND.Services
{
    public interface IRoleAccessService
    {
        Task<int> InserUpdateRoleAccess(RoleMenuAccess role);
        Task<int> InserUpdateRole(RoleModel role);
        Task<DataTable> GetRoles(DocRequest model);
    }
    public class RoleAccessService:IRoleAccessService
    {
        private readonly RoleAccessDBHelper _sqlDBHelper;
        public RoleAccessService(RoleAccessDBHelper sqlDBHelper)
        {
            _sqlDBHelper = sqlDBHelper;
        }
        public async Task<int> InserUpdateRoleAccess(RoleMenuAccess role)
        {
            return await _sqlDBHelper.InserUpdateRoleAccess(role);
        }
        public async Task<int> InserUpdateRole(RoleModel role)
        {
            return await _sqlDBHelper.InserUpdateRole(role);
        }
        public async Task<DataTable> GetRoles(DocRequest model)
        {
            return await _sqlDBHelper.GetRoles(model);
        }
    }
}
