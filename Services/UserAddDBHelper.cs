using Npgsql;
using OCR_BACKEND.Modals;
using System.Data;

namespace OCR_BACKEND.Services
{
    public class UserAddDBHelper
    {
        private readonly SqlDBHelper _sqlDBHelper;

        public UserAddDBHelper(SqlDBHelper sqlDBHelper)
        {
            _sqlDBHelper = sqlDBHelper;
        }

        public async Task<int> InsertUpdateUserAsync(UserRequest user)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("p_userid", user.UserId),
                new NpgsqlParameter("p_firstname", user.FirstName),
                new NpgsqlParameter("p_middlename", user.MiddleName),
                new NpgsqlParameter("p_lastname", user.LastName),
                new NpgsqlParameter("p_mobile", user.Mobile),
                new NpgsqlParameter("p_email", user.Email),
                new NpgsqlParameter("p_username", user.UserName),
                new NpgsqlParameter("p_userpass", user.UserPass), 
                new NpgsqlParameter("p_roleid", user.RoleId),
                new NpgsqlParameter("p_isactive", user.IsActive),
                new NpgsqlParameter("p_createdby", user.CreatedBy)
            };

            string query = "SELECT fn_usersInsertUpdate(@p_userid,@p_firstname,@p_middlename,@p_lastname,@p_mobile,@p_email,@p_username,@p_userpass,@p_roleid,@p_isactive,@p_createdby)";

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);

            if (await reader.ReadAsync())
                return reader.GetInt32(0);

            return 0;
        }
        public async Task<DataTable> UsersGET(PaginationRequest model)
        {
            NpgsqlParameter[] parameters = new NpgsqlParameter[]
            {
            new NpgsqlParameter("p_userid", model.UserId),
            new NpgsqlParameter("p_startindex", Convert.ToInt32(model.StartIndex)),
            new NpgsqlParameter("p_pagesize", Convert.ToInt32(model.PageSize)),
            new NpgsqlParameter("p_searchby", Convert.ToInt32(model.SearchBy)),
            new NpgsqlParameter("p_searchcriteria", model.SearchCriteria ?? "")
            };

            using (DataTable dt = await _sqlDBHelper.ExecuteDataTableWithParametersAsync(
                "SELECT * FROM UsersGet(@p_userid,@p_startindex,@p_pagesize,@p_searchby,@p_searchcriteria)", 
                parameters))
            {
                return dt.Copy();
            }
        }
    }
}
