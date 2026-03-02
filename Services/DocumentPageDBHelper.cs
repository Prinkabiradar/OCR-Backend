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
                new NpgsqlParameter("p_userid", model.CreatedBy)
            };

            string query = "SELECT insertupdate_documentpage(@p_documentpageid,@p_documentid,@p_pagenumber,@p_extractedtext,@p_statusid,@p_userid)";

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);

            if (await reader.ReadAsync())
                return reader.GetInt32(0);

            return 0;
        }
        public async Task<DataTable> GetDocumentPagesByDocument(int documentId)
        {
            DataTable dt = new DataTable();

            string query = "SELECT * FROM fn_documentpage_getbydocument(@p_documentid)";

            var parameters = new[]
            {
            new NpgsqlParameter("p_documentid", documentId)
            };

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);

            dt.Load(reader);

            return dt;
        }
    }
}