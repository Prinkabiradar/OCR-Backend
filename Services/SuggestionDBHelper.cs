using Npgsql;
using System.Data;
using OCR_BACKEND.Modals;

namespace OCR_BACKEND.Services
{
    public class SuggestionDBHelper
    {
        private readonly SqlDBHelper _sqlDBHelper;

        public SuggestionDBHelper(SqlDBHelper sqlDBHelper)
        {
            _sqlDBHelper = sqlDBHelper;
        }

        // INSERT
        public async Task<int> InsertPageSuggestion(SuggestionRequest model)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("p_suggestionid",model.SuggestionId),
                new NpgsqlParameter("p_documentpageid", model.DocumentPageId),
                new NpgsqlParameter("p_documentid", model.DocumentId),
                new NpgsqlParameter("p_pagenumber", model.PageNumber),
                new NpgsqlParameter("p_suggestiontext", model.SuggestionText),
                new NpgsqlParameter("p_createdby", model.CreatedBy)
            };

            string query = @"SELECT fn_insert_page_suggestion(
                                @p_suggestionid,
                                @p_documentpageid,
                                @p_documentid,
                                @p_pagenumber,
                                @p_suggestiontext,
                                @p_createdby)";

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);

            if (await reader.ReadAsync())
                return reader.GetInt32(0);

            return 0;
        }

        // GET ACTIVE
        public async Task<DataTable> GetActiveSuggestion( DocumentFetchRequest model)
        {
            DataTable dt = new DataTable();

            var parameters = new[]
            {
                new NpgsqlParameter("p_documentpageid", model.documentPageId),
                new NpgsqlParameter("p_startindex", model.StartIndex),
                new NpgsqlParameter("p_pagesize", model.PageSize),
                new NpgsqlParameter("p_searchby", (object?)model.SearchBy ?? DBNull.Value),
                new NpgsqlParameter("p_searchcriteria", (object?)model.SearchCriteria ?? DBNull.Value)

            };

            string query = @"SELECT * FROM fn_get_active_suggestion(@p_documentpageid,@p_startindex, @p_pagesize, @p_searchby, @p_searchcriteria)";

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);
            dt.Load(reader);

            return dt;
        }

        // REVIEW (ACCEPT / REJECT)
        public async Task<int> ReviewSuggestion(int suggestionId, int documentPageId, string action, int reviewedBy, int roleId)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("p_suggestionid", suggestionId),
                new NpgsqlParameter("p_documentpageid", documentPageId),
                new NpgsqlParameter("p_action", action),
                new NpgsqlParameter("p_reviewedby", reviewedBy),
                new NpgsqlParameter("p_roleid", roleId)
            };

            string query = @"SELECT fn_review_page_suggestion(
                                @p_suggestionid,
                                @p_documentpageid,
                                @p_action,
                                @p_reviewedby,
                                @p_roleid)";

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);

            if (await reader.ReadAsync())
                return reader.GetInt32(0);

            return 0;
        }
    }
}