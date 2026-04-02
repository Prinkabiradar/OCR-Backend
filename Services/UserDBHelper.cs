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
            new NpgsqlParameter("p_userpass", password)  
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
 

        public async Task<User?> GetUserByEmailOrMobileAsync(string input)
        {
            var parameters = new[]
            {
        new NpgsqlParameter("p_input", input)
    };
            string query = "SELECT * FROM fn_get_user_by_email_or_mobile(@p_input)";
            using var reader = await _db.ExecuteReaderAsync(query, parameters);
            if (await reader.ReadAsync())
            {
                return new User
                {
                    UserId = reader.GetInt32(reader.GetOrdinal("userid")),
                    UserName = reader.GetString(reader.GetOrdinal("username")),
                    Email = reader.GetString(reader.GetOrdinal("email")),
                    Mobile = reader.IsDBNull(reader.GetOrdinal("mobile"))
                                   ? null
                                   : reader.GetString(reader.GetOrdinal("mobile")),
                };
            }
            return null;
        }

        public async Task SaveOtpAsync(int userId, string otp, DateTime expiresAt)
        {
            var parameters = new[]
            {
        new NpgsqlParameter("p_userid",  NpgsqlTypes.NpgsqlDbType.Integer) { Value = userId },
        new NpgsqlParameter("p_otp",     NpgsqlTypes.NpgsqlDbType.Varchar) { Value = otp },
        new NpgsqlParameter("p_expires", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = expiresAt }  // ← explicit type
    };
            await _db.ExecuteNonQueryAsync(
                "SELECT fn_save_reset_otp(@p_userid, @p_otp, @p_expires)", parameters);
        }

        public async Task<bool> VerifyOtpAsync(int userId, string otp)
        {
            var parameters = new[]
            {
        new NpgsqlParameter("p_userid", userId),
        new NpgsqlParameter("p_otp",    otp)
    };
            using var reader = await _db.ExecuteReaderAsync(
                "SELECT fn_verify_reset_otp(@p_userid, @p_otp)", parameters);
            if (await reader.ReadAsync())
                return reader.GetBoolean(0);
            return false;
        }

        public async Task ResetPasswordByUserIdAsync(int userId, string hashedPassword)
        {
            var parameters = new[]
            {
        new NpgsqlParameter("p_userid",  userId),
        new NpgsqlParameter("p_newpass", hashedPassword)
    };
            await _db.ExecuteNonQueryAsync(
                "SELECT fn_reset_password_by_userid(@p_userid, @p_newpass)", parameters);
        }
    }
}
