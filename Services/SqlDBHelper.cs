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
        public NpgsqlConnection CreateConnection() => new NpgsqlConnection(_connectionString);
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
        
        public async Task<DataTable> ExecuteDataTableWithParametersAsync(
            string functionName,
            NpgsqlParameter[] parameters)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new NpgsqlCommand(functionName, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddRange(parameters);

            using var reader = await cmd.ExecuteReaderAsync();

            var dt = new DataTable();
            dt.Load(reader);

            return dt;
        }
        public async Task<DataTable> ExecuteFunctionAsync(string functionName, NpgsqlParameter[] parameters)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            // Build parameter placeholders
            var paramPlaceholders = string.Join(", ", parameters.Select(p => "@" + p.ParameterName));

            var query = $"SELECT * FROM public.{functionName}({paramPlaceholders})";

            using var cmd = new NpgsqlCommand(query, conn);

            cmd.Parameters.AddRange(parameters);

            using var reader = await cmd.ExecuteReaderAsync();

            var dt = new DataTable();
            dt.Load(reader);

            return dt;
        }
        public async Task ExecuteNonQueryAsync(string query, NpgsqlParameter[] parameters)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new NpgsqlCommand(query, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddRange(parameters);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
