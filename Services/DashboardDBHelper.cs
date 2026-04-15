using Npgsql;
using OCR_BACKEND.Modals;
using System.Text.Json;

namespace OCR_BACKEND.Services
{
    public class DashboardDBHelper
    {
        private readonly SqlDBHelper _sqlDBHelper;

        public DashboardDBHelper(SqlDBHelper sqlDBHelper)
        {
            _sqlDBHelper = sqlDBHelper;
        }

        // ── SP7 : get_full_dashboard — single DB call, returns JSON ───────────
        public async Task<DashboardResponse> GetFullDashboard()
        {
            string sql = "SELECT public.get_full_dashboard()";
            var dt = await _sqlDBHelper.ExecuteDataTableWithParametersAsync(
                sql, Array.Empty<NpgsqlParameter>()
            );

            var json = dt.Rows[0][0].ToString()!;

            var result = JsonSerializer.Deserialize<DashboardResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new DashboardResponse();

            return result;
        }
    }
}