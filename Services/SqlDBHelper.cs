using Npgsql;
using System.Data;

namespace OCR_BACKEND.Services
{
    public class SqlDBHelper
    {
        private readonly string _connectionString;

        public SqlDBHelper(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("PostgresConnection");
        }

        public async Task<NpgsqlDataReader> ExecuteReaderAsync(
            string functionName,
            NpgsqlParameter[] parameters)
        {
            var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(functionName, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddRange(parameters);

            return await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection);
        }
    }
}
