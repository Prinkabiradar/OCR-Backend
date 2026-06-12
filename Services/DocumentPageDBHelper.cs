using Npgsql;
using OCR_BACKEND.Modals;
using System.Data;

namespace OCR_BACKEND.Services
{
    public class DocumentPageDBHelper
    {
        private readonly SqlDBHelper _sqlDBHelper;

        public DocumentPageDBHelper(SqlDBHelper sqlDBHelper)
        {
            _sqlDBHelper = sqlDBHelper;
        }

        public async Task<int> InsertUpdateDocumentPage(DocumentPageRequest model)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("p_documentpageid", model.DocumentPageId),
                new NpgsqlParameter("p_documentid", model.DocumentId),
                new NpgsqlParameter("p_pagenumber", model.PageNumber),
                new NpgsqlParameter("p_extractedtext", model.ExtractedText),
                new NpgsqlParameter("p_statusid", model.StatusId),
                new NpgsqlParameter("p_userid", model.UserId),
                new NpgsqlParameter("p_roleid", model.RoleId),
                new NpgsqlParameter("p_rejectreason", model.RejectionReason ?? (object)DBNull.Value),
                new NpgsqlParameter("p_job_id", model.job_id),
                new NpgsqlParameter("p_file_name", model.file_name ?? (object)DBNull.Value),
            };

            string query = "SELECT insertupdate_documentpage(@p_documentpageid,@p_documentid,@p_pagenumber,@p_extractedtext,@p_statusid,@p_userid,@p_roleid,@p_rejectreason, @p_job_id, @p_file_name)";

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);

            if (await reader.ReadAsync())
                return reader.GetInt32(0);

            return 0;
        }
        public async Task<DataTable> GetDocumentPagesByDocument(OcrDocumentRequest request)
        {
            DataTable dt = new DataTable();

            string query = @"SELECT * 
                     FROM fn_documentpage_getbydocument(
                        @p_documentid,
                        @p_startindex,
                        @p_pagesize,
                        @p_searchby,
                        @p_searchcriteria,
                        @p_roleid)";

            var parameters = new[]
            {
        new NpgsqlParameter("p_documentid", request.DocumentId),
        new NpgsqlParameter("p_startindex", request.StartIndex),
        new NpgsqlParameter("p_pagesize", request.PageSize),
        new NpgsqlParameter("p_searchby", request.SearchBy ?? (object)DBNull.Value),
        new NpgsqlParameter("p_searchcriteria", request.SearchCriteria ?? (object)DBNull.Value),
        new NpgsqlParameter("p_roleid", request.RoleId)
    };

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);

            dt.Load(reader);

            return dt;
        }

        public async Task<bool> DeleteDocumentPage(int documentPageId, int userId)
        {
            try
            {
                var parameters = new[]
                {
                    new NpgsqlParameter("p_documentpageid", documentPageId),
                    new NpgsqlParameter("p_userid", userId),
                };

                using var reader = await _sqlDBHelper.ExecuteReaderAsync(
                    "SELECT public.delete_documentpage(@p_documentpageid, @p_userid)",
                    parameters);

                if (await reader.ReadAsync())
                {
                    var value = reader.GetValue(0);
                    if (value is bool boolValue) return boolValue;
                    if (int.TryParse(value?.ToString(), out var intValue)) return intValue > 0;
                }
            }
            catch (PostgresException ex) when (
                ex.SqlState == PostgresErrorCodes.UndefinedFunction ||
                ex.SqlState == PostgresErrorCodes.UndefinedTable ||
                ex.SqlState == PostgresErrorCodes.UndefinedColumn)
            {
                // Fall back to direct table updates when the database function is missing
                // or points at an outdated table/column name.
            }

            await using (var conn = _sqlDBHelper.CreateConnection())
            {
                await conn.OpenAsync();
                var table = await ResolveDocumentPageTableAsync(conn);
                if (table == null)
                    throw new InvalidOperationException("Could not find the document page table in the database schema.");

                var tableName = $"{QuoteIdentifier(table.Value.SchemaName)}.{QuoteIdentifier(table.Value.TableName)}";
                var idColumn = QuoteIdentifier(table.Value.IdColumnName);

                if (!string.IsNullOrWhiteSpace(table.Value.IsActiveColumnName))
                {
                    await using var softDeleteCmd = new NpgsqlCommand(
                        $@"UPDATE {tableName}
                              SET {QuoteIdentifier(table.Value.IsActiveColumnName)} = false
                            WHERE {idColumn} = @p_documentpageid",
                        conn);
                    softDeleteCmd.Parameters.AddWithValue("p_documentpageid", documentPageId);

                    var affected = await softDeleteCmd.ExecuteNonQueryAsync();
                    if (affected > 0) return true;
                }

                await using var hardDeleteCmd = new NpgsqlCommand(
                    $@"DELETE FROM {tableName}
                        WHERE {idColumn} = @p_documentpageid",
                    conn);
                hardDeleteCmd.Parameters.AddWithValue("p_documentpageid", documentPageId);

                return await hardDeleteCmd.ExecuteNonQueryAsync() > 0;
            }
        }

        private async Task<(string SchemaName, string TableName, string IdColumnName, string? IsActiveColumnName)?> ResolveDocumentPageTableAsync(NpgsqlConnection conn)
        {
            const string query = @"
                SELECT
                    table_schema,
                    table_name,
                    MAX(CASE
                        WHEN lower(column_name) IN ('documentpageid', 'document_page_id')
                        THEN column_name
                    END) AS id_column,
                    MAX(CASE
                        WHEN lower(column_name) IN ('isactive', 'is_active')
                        THEN column_name
                    END) AS is_active_column
                FROM information_schema.columns
                WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
                GROUP BY table_schema, table_name
                HAVING MAX(CASE
                    WHEN lower(column_name) IN ('documentpageid', 'document_page_id')
                    THEN column_name
                END) IS NOT NULL
                ORDER BY
                    CASE WHEN table_schema = 'public' THEN 0 ELSE 1 END,
                    CASE lower(table_name)
                        WHEN 'documentpage' THEN 0
                        WHEN 'document_page' THEN 1
                        WHEN 'documentpages' THEN 2
                        WHEN 'document_pages' THEN 3
                        WHEN 'tbl_documentpage' THEN 4
                        WHEN 'tbl_document_page' THEN 5
                        ELSE 6
                    END,
                    table_name
                LIMIT 1";

            await using var cmd = new NpgsqlCommand(query, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return (
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)
            );
        }

        private static string QuoteIdentifier(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public async Task<DataTable> GetDocumentsByDocumentType(DocumentFetchRequest model)
        {
            DataTable dt = new DataTable();
            string query = @"SELECT * FROM public.fn_document_getbydocumenttype(
                                @p_documenttypeid,
                                @p_startindex,
                                @p_pagesize,
                                @p_searchby,
                                @p_searchcriteria,
                                @p_roleid)";

            var parameters = new[]
            {
                new NpgsqlParameter("p_documenttypeid", model.DocumentTypeId),
                new NpgsqlParameter("p_startindex", model.StartIndex),
                new NpgsqlParameter("p_pagesize", model.PageSize),
                new NpgsqlParameter("p_searchby", (object?)model.SearchBy ?? DBNull.Value),
                new NpgsqlParameter("p_searchcriteria", (object?)model.SearchCriteria ?? DBNull.Value),
                new NpgsqlParameter("p_roleid", model.RoleId)
            };

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);
            dt.Load(reader);
            return dt;
        }

        public async Task<DataTable> GetSuggestionPages(SuggestionPageRequest request)
        {
            DataTable dt = new DataTable();

            string query = @"SELECT * 
                     FROM fn_getsuggesteddocument(
                        @p_documentid,
                        @p_documentpageid,
                        @p_startindex,
                        @p_pagesize,
                        @p_searchby,
                        @p_searchcriteria,
                        @p_roleid)";

            var parameters = new[]
            {
        new NpgsqlParameter("p_documentid", request.DocumentId),
        new NpgsqlParameter("p_documentpageid", request.DocumentPageId),
        new NpgsqlParameter("p_startindex", request.StartIndex),
        new NpgsqlParameter("p_pagesize", request.PageSize),
        new NpgsqlParameter("p_searchby", request.SearchBy ?? (object)DBNull.Value),
        new NpgsqlParameter("p_searchcriteria", request.SearchCriteria ?? (object)DBNull.Value),
        new NpgsqlParameter("p_roleid", request.RoleId)
    };

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);

            dt.Load(reader);

            return dt;
        }
    }
}
