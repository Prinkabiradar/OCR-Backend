using Npgsql;
using OCR_BACKEND.Modals;

namespace OCR_BACKEND.Services
{
    public class UserDBHelper
    {
        private readonly SqlDBHelper _db;

        public UserDBHelper(SqlDBHelper db)
        {
            _db = db;
        }

        public async Task<User> AuthenticateUserAsync(string username, string password)
        {
            var parameters = new[]
            {
            new NpgsqlParameter("p_username", username),
            new NpgsqlParameter("p_userpass", password) // hash later
        };

            string query = "SELECT * FROM fn_authenticate_user(@p_username, @p_userpass)";

            using var reader = await _db.ExecuteReaderAsync(query, parameters);

            if (await reader.ReadAsync())
            {
                return new User
                {
                    UserId = reader.GetInt32(reader.GetOrdinal("userid")),
                    UserName = reader.GetString(reader.GetOrdinal("username")),
                    Email = reader.GetString(reader.GetOrdinal("email")),
                    RoleId = reader.GetInt32(reader.GetOrdinal("roleid")),
                    UserPass = reader.GetString(reader.GetOrdinal("userpass")),
                    IsActive = reader.GetBoolean(reader.GetOrdinal("isactive"))
                };
            }

            return null;
        }
    }
}
