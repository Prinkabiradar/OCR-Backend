using Npgsql;
using OCR_BACKEND.Modals;

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
    }
}