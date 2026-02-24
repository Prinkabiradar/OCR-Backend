using Npgsql;
using System.Data;
using OCR_BACKEND.Modals;

namespace OCR_BACKEND.Services
{
    public class DocumentTypeDBHelper
    {
        private readonly SqlDBHelper _sqlDBHelper;

        public DocumentTypeDBHelper(SqlDBHelper sqlDBHelper)
        {
            _sqlDBHelper = sqlDBHelper;
        }

        public async Task<int> InsertUpdateDocumentType(DocumentTypeRequest model)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("p_documenttypeid", model.DocumentTypeId),
                new NpgsqlParameter("p_documenttypename", model.DocumentTypeName),
                new NpgsqlParameter("p_isactive", model.IsActive),
                new NpgsqlParameter("p_userid", model.CreatedBy)
            };

            string query = "SELECT InsertUpdateDocumentType(@p_documenttypeid,@p_documenttypename,@p_isactive,@p_userid)";

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);

            if (await reader.ReadAsync())
                return reader.GetInt32(0);

            return 0;
        }
    }
}