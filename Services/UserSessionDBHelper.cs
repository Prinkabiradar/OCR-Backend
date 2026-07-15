using Npgsql;
using OCR_BACKEND.Modals;
using System.Data;

namespace OCR_BACKEND.Services
{
    public class UserSessionDBHelper
    {
        private readonly SqlDBHelper _db;

        public UserSessionDBHelper(SqlDBHelper db)
        {
            _db = db;
        }

        public async Task<Guid> RecordLoginAsync(int userId)
        {
            await EnsureSessionTableAsync();

            var sessionId = Guid.NewGuid();
            var parameters = new[]
            {
                new NpgsqlParameter("p_sessionid", sessionId),
                new NpgsqlParameter("p_userid", userId)
            };

            await _db.ExecuteNonQueryAsync(
                @"INSERT INTO public.user_sessions (session_id, user_id, login_time)
                  VALUES (@p_sessionid, @p_userid, NOW())",
                parameters);

            return sessionId;
        }

        public async Task RecordLogoutAsync(int userId, Guid? sessionId)
        {
            await EnsureSessionTableAsync();

            if (sessionId.HasValue)
            {
                await _db.ExecuteNonQueryAsync(
                    @"UPDATE public.user_sessions
                         SET logout_time = NOW()
                       WHERE session_id = @p_sessionid
                         AND user_id = @p_userid
                         AND logout_time IS NULL",
                    new[]
                    {
                        new NpgsqlParameter("p_sessionid", sessionId.Value),
                        new NpgsqlParameter("p_userid", userId)
                    });
                return;
            }

            await _db.ExecuteNonQueryAsync(
                @"UPDATE public.user_sessions
                     SET logout_time = NOW()
                   WHERE session_id = (
                        SELECT session_id
                          FROM public.user_sessions
                         WHERE user_id = @p_userid
                           AND logout_time IS NULL
                         ORDER BY login_time DESC
                         LIMIT 1
                   )",
                new[] { new NpgsqlParameter("p_userid", userId) });
        }

        public async Task<ProductivityReport> GetProductivityReportAsync(DateOnly reportDate, int[] completedStatusIds)
        {
            await EnsureSessionTableAsync();

            await using var conn = _db.CreateConnection();
            await conn.OpenAsync();

            var usersTable = await ResolveTableAsync(conn, new[] { "userid", "user_id" }, new[] { "users", "user", "tbl_users" });
            var pagesTable = await ResolveTableAsync(conn, new[] { "documentpageid", "document_page_id" }, new[] { "documentpage", "document_page", "documentpages", "document_pages", "tbl_documentpage" });

            if (usersTable == null)
                throw new InvalidOperationException("Could not find users table for productivity report.");

            var users = await LoadUsersAsync(conn, usersTable.Value);
            var pageCounts = pagesTable == null
                ? new Dictionary<int, (long DocsToday, long DocsOverall, long PagesToday, long PagesOverall)>()
                : await LoadPageCountsAsync(conn, pagesTable.Value, reportDate, completedStatusIds);
            var sessionTimes = await LoadSessionTimesAsync(conn, reportDate);

            var report = new ProductivityReport
            {
                ReportDate = reportDate,
                GeneratedAt = DateTime.Now
            };

            foreach (var user in users)
            {
                pageCounts.TryGetValue(user.UserId, out var counts);
                sessionTimes.TryGetValue(user.UserId, out var sessions);

                report.Users.Add(new UserProductivitySummary
                {
                    UserId = user.UserId,
                    UserName = user.UserName,
                    Email = user.Email,
                    Mobile = user.Mobile,
                    FirstLoginTime = sessions.FirstLogin,
                    LastLogoutTime = sessions.LastLogout,
                    DocumentsProcessedToday = counts.DocsToday,
                    DocumentsProcessedOverall = counts.DocsOverall,
                    PagesCompletedToday = counts.PagesToday,
                    PagesCompletedOverall = counts.PagesOverall
                });
            }

            report.TotalDocumentsProcessedToday = report.Users.Sum(x => x.DocumentsProcessedToday);
            report.TotalDocumentsProcessedOverall = report.Users.Sum(x => x.DocumentsProcessedOverall);
            report.TotalPagesCompletedToday = report.Users.Sum(x => x.PagesCompletedToday);
            report.TotalPagesCompletedOverall = report.Users.Sum(x => x.PagesCompletedOverall);

            return report;
        }

        private async Task EnsureSessionTableAsync()
        {
            await _db.ExecuteNonQueryAsync(
                @"CREATE TABLE IF NOT EXISTS public.user_sessions
                  (
                      session_id UUID PRIMARY KEY,
                      user_id INTEGER NOT NULL,
                      login_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                      logout_time TIMESTAMPTZ NULL
                  );

                  CREATE INDEX IF NOT EXISTS ix_user_sessions_user_login
                      ON public.user_sessions (user_id, login_time DESC);",
                Array.Empty<NpgsqlParameter>());
        }

        private static async Task<List<UserProductivitySummary>> LoadUsersAsync(
            NpgsqlConnection conn,
            (string Schema, string Table, string IdColumn) usersTable)
        {
            var tableName = $"{QuoteIdentifier(usersTable.Schema)}.{QuoteIdentifier(usersTable.Table)}";
            var idColumn = QuoteIdentifier(usersTable.IdColumn);
            var userNameColumn = await ResolveColumnAsync(conn, usersTable, new[] { "username", "user_name", "firstname", "first_name" });
            var emailColumn = await ResolveColumnAsync(conn, usersTable, new[] { "email", "emailid", "email_id" });
            var mobileColumn = await ResolveColumnAsync(conn, usersTable, new[] { "mobile", "mobileno", "mobile_no", "phone" });
            var isActiveColumn = await ResolveColumnAsync(conn, usersTable, new[] { "isactive", "is_active" });

            var query = $@"
                SELECT
                    {idColumn} AS userid,
                    {(userNameColumn == null ? "''" : QuoteIdentifier(userNameColumn))} AS username,
                    {(emailColumn == null ? "NULL" : QuoteIdentifier(emailColumn))} AS email,
                    {(mobileColumn == null ? "NULL" : QuoteIdentifier(mobileColumn))} AS mobile
                FROM {tableName}
                WHERE {(isActiveColumn == null ? "TRUE" : $"COALESCE({QuoteIdentifier(isActiveColumn)}, TRUE) = TRUE")}
                ORDER BY username";

            await using var cmd = new NpgsqlCommand(query, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            var users = new List<UserProductivitySummary>();
            while (await reader.ReadAsync())
            {
                users.Add(new UserProductivitySummary
                {
                    UserId = Convert.ToInt32(reader["userid"]),
                    UserName = Convert.ToString(reader["username"]) ?? string.Empty,
                    Email = reader["email"] == DBNull.Value ? null : Convert.ToString(reader["email"]),
                    Mobile = reader["mobile"] == DBNull.Value ? null : Convert.ToString(reader["mobile"])
                });
            }

            return users;
        }

        private static async Task<Dictionary<int, (long DocsToday, long DocsOverall, long PagesToday, long PagesOverall)>> LoadPageCountsAsync(
            NpgsqlConnection conn,
            (string Schema, string Table, string IdColumn) pagesTable,
            DateOnly reportDate,
            int[] completedStatusIds)
        {
            var tableName = $"{QuoteIdentifier(pagesTable.Schema)}.{QuoteIdentifier(pagesTable.Table)}";
            var userColumn = await ResolveColumnAsync(conn, pagesTable, new[] { "userid", "user_id", "updatedby", "updated_by", "createdby", "created_by" });
            var documentColumn = await ResolveColumnAsync(conn, pagesTable, new[] { "documentid", "document_id" });
            var statusColumn = await ResolveColumnAsync(conn, pagesTable, new[] { "statusid", "status_id" });
            var dateColumn = await ResolveColumnAsync(conn, pagesTable, new[] { "updateddate", "updated_date", "modifieddate", "modified_date", "createddate", "created_date", "createdat", "created_at" });

            if (userColumn == null || documentColumn == null)
                return new Dictionary<int, (long, long, long, long)>();

            var statusFilter = statusColumn != null && completedStatusIds.Length > 0
                ? $"AND {QuoteIdentifier(statusColumn)} = ANY(@p_statusids)"
                : string.Empty;
            var dateExpression = dateColumn == null ? "NOW()" : QuoteIdentifier(dateColumn);

            var query = $@"
                SELECT
                    {QuoteIdentifier(userColumn)} AS userid,
                    COUNT(DISTINCT CASE
                        WHEN {dateExpression} >= @p_startdate AND {dateExpression} < @p_enddate
                        THEN {QuoteIdentifier(documentColumn)}
                    END) AS docs_today,
                    COUNT(DISTINCT {QuoteIdentifier(documentColumn)}) AS docs_overall,
                    COUNT(CASE
                        WHEN {dateExpression} >= @p_startdate AND {dateExpression} < @p_enddate
                        THEN 1
                    END) AS pages_today,
                    COUNT(*) AS pages_overall
                FROM {tableName}
                WHERE {QuoteIdentifier(userColumn)} IS NOT NULL
                  {statusFilter}
                GROUP BY {QuoteIdentifier(userColumn)}";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("p_startdate", reportDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("p_enddate", reportDate.AddDays(1).ToDateTime(TimeOnly.MinValue));
            if (!string.IsNullOrWhiteSpace(statusFilter))
                cmd.Parameters.AddWithValue("p_statusids", completedStatusIds);

            await using var reader = await cmd.ExecuteReaderAsync();
            var counts = new Dictionary<int, (long, long, long, long)>();
            while (await reader.ReadAsync())
            {
                counts[Convert.ToInt32(reader["userid"])] = (
                    Convert.ToInt64(reader["docs_today"]),
                    Convert.ToInt64(reader["docs_overall"]),
                    Convert.ToInt64(reader["pages_today"]),
                    Convert.ToInt64(reader["pages_overall"]));
            }

            return counts;
        }

        private static async Task<Dictionary<int, (DateTime? FirstLogin, DateTime? LastLogout)>> LoadSessionTimesAsync(
            NpgsqlConnection conn,
            DateOnly reportDate)
        {
            const string query = @"
                SELECT
                    user_id,
                    MIN(login_time) AS first_login,
                    MAX(logout_time) AS last_logout
                FROM public.user_sessions
                WHERE login_time >= @p_startdate
                  AND login_time < @p_enddate
                GROUP BY user_id";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("p_startdate", reportDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("p_enddate", reportDate.AddDays(1).ToDateTime(TimeOnly.MinValue));

            await using var reader = await cmd.ExecuteReaderAsync();
            var sessions = new Dictionary<int, (DateTime?, DateTime?)>();
            while (await reader.ReadAsync())
            {
                sessions[reader.GetInt32("user_id")] = (
                    reader["first_login"] == DBNull.Value ? null : Convert.ToDateTime(reader["first_login"]),
                    reader["last_logout"] == DBNull.Value ? null : Convert.ToDateTime(reader["last_logout"]));
            }

            return sessions;
        }

        private static async Task<(string Schema, string Table, string IdColumn)?> ResolveTableAsync(
            NpgsqlConnection conn,
            string[] idColumnNames,
            string[] preferredTableNames)
        {
            const string query = @"
                SELECT table_schema, table_name, column_name
                FROM information_schema.columns
                WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
                  AND lower(column_name) = ANY(@p_columns)
                ORDER BY
                    CASE WHEN table_schema = 'public' THEN 0 ELSE 1 END,
                    array_position(@p_tables, lower(table_name)),
                    table_name
                LIMIT 1";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("p_columns", idColumnNames);
            cmd.Parameters.AddWithValue("p_tables", preferredTableNames);
            await using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync()) return null;

            return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
        }

        private static async Task<string?> ResolveColumnAsync(
            NpgsqlConnection conn,
            (string Schema, string Table, string IdColumn) table,
            string[] columnNames)
        {
            const string query = @"
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = @p_schema
                  AND table_name = @p_table
                  AND lower(column_name) = ANY(@p_columns)
                ORDER BY array_position(@p_columns, lower(column_name))
                LIMIT 1";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("p_schema", table.Schema);
            cmd.Parameters.AddWithValue("p_table", table.Table);
            cmd.Parameters.AddWithValue("p_columns", columnNames);
            var value = await cmd.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        private static string QuoteIdentifier(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
