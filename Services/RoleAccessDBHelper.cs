using Npgsql;
using OCR_BACKEND.Modals;
using System.Data;

namespace OCR_BACKEND.Services
{
    public class RoleAccessDBHelper
    {
        private SqlDBHelper _sqlDBHelper;

        public RoleAccessDBHelper(SqlDBHelper sqlDBHelper) 
        {
            _sqlDBHelper= sqlDBHelper;
        }
        public async Task<int> InserUpdateRoleAccess(RoleMenuAccess model)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("p_roleaccessid", model.RoleAccessId),
                new NpgsqlParameter("p_roleid", model.RoleId),
                new NpgsqlParameter("p_menuid", model.MenuId),
                new NpgsqlParameter("p_canview", model.CanView),
                new NpgsqlParameter("p_caninsert", model.CanInsert),
                new NpgsqlParameter("p_canupdate",model.CanUpdate),
                new NpgsqlParameter("p_candelete", model.CanDelete)
            };

            string query = "SELECT RoleMenuAccess_Save(@p_roleaccessid,@p_roleid,@p_menuid,@p_canview,@p_caninsert,@p_canupdate,@p_candelete)";

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);

            if (await reader.ReadAsync())
                return reader.GetInt32(0);

            return 0;
        }

        public async Task<int> InserUpdateRole(RoleModel model)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("p_roleid", model.RoleId),
                new NpgsqlParameter("p_rolecode", model.RoleCode),
                new NpgsqlParameter("p_rolename", model.RoleName),
                new NpgsqlParameter("p_roledescription", model.RoleDescription),
                new NpgsqlParameter("p_createdby", model.UserId),
                new NpgsqlParameter("p_updatedby",model.UserId),
                new NpgsqlParameter("p_isactive", model.IsActive)
            };

            string query = "SELECT insertupdate_role(@p_roleid,@p_rolecode,@p_rolename,@p_roledescription,@p_createdby,@p_updatedby,@p_isactive)";

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);

            if (await reader.ReadAsync())
                return reader.GetInt32(0);

            return 0;
        }
        public async Task<DataTable> GetRoles(DocRequest model)
        {
            DataTable dt = new DataTable();
            string query = @"SELECT * FROM fn_roles_get(@p_startindex, @p_pagesize, @p_searchby, @p_searchcriteria)";

            var parameters = new[]
            {
                new NpgsqlParameter("p_startindex", model.StartIndex),
                new NpgsqlParameter("p_pagesize", model.PageSize),
                new NpgsqlParameter("p_searchby", (object?)model.SearchBy ?? DBNull.Value),
                new NpgsqlParameter("p_searchcriteria", (object?)model.SearchCriteria ?? DBNull.Value)
            };

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);
            dt.Load(reader);
            return dt;
        }
    }
}
