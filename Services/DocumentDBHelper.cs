using Npgsql;
using OCR_BACKEND.Modals;
using System.Data;

namespace OCR_BACKEND.Services
{
    public class DocumentDBHelper
    {
        private readonly SqlDBHelper _sqlDBHelper;

        public DocumentDBHelper(SqlDBHelper sqlDBHelper)
        {
            _sqlDBHelper = sqlDBHelper;
        }

        public async Task<int> InsertUpdateDocument(DocumentRequest model)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("p_documentid", model.DocumentId),
                new NpgsqlParameter("p_documenttypeid", model.DocumentTypeId),
                new NpgsqlParameter("p_documentname", model.DocumentName),
                new NpgsqlParameter("p_totalpages", model.TotalPages),
                new NpgsqlParameter("p_userid", model.CreatedBy)
            };

            string query = "SELECT insertupdate_document(@p_documentid,@p_documenttypeid,@p_documentname,@p_totalpages,@p_userid)";

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);

            if (await reader.ReadAsync())
                return reader.GetInt32(0);

            return 0;
        }

        public async Task<DataTable> GetDocuments(DocumentFetchRequest model)
        {
            DataTable dt = new DataTable();
            string query = @"SELECT * FROM fn_document_get(@p_startindex, @p_pagesize, @p_searchby, @p_searchcriteria)";

            var parameters = new[]
            {
                new NpgsqlParameter("p_startindex", model.StartIndex),
                new NpgsqlParameter("p_pagesize", model.PageSize),
                new NpgsqlParameter("p_searchby", (object?)model.SearchBy ?? DBNull.Value),
                new NpgsqlParameter("p_searchcriteria", (object?)model.SearchCriteria ?? DBNull.Value)
            };

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);
            dt.Load(reader);
            return dt;
        }
        public async Task<bool> ManageDocumentLock(ManageLockRequest model)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("p_documentid", model.DocumentId),
                new NpgsqlParameter("p_userid", model.UserId),
                new NpgsqlParameter("p_action", model.Action)
            };

            string query = "SELECT public.fn_manage_document_lock(@p_documentid,@p_userid,@p_action)";

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);

            if (await reader.ReadAsync())
                return reader.GetBoolean(0);

            return false;
        }
    }
}