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
                new NpgsqlParameter("p_rejectreason", model.RejectionReason ?? (object)DBNull.Value)
            };

            string query = "SELECT insertupdate_documentpage(@p_documentpageid,@p_documentid,@p_pagenumber,@p_extractedtext,@p_statusid,@p_userid,@p_roleid,@p_rejectreason)";

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