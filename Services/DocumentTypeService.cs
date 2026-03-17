using OCR_BACKEND.Modals;
using System.Data;

namespace OCR_BACKEND.Services
{
    public interface IDocumentTypeService
    {
        Task<int> InsertUpdateDocumentType(DocumentTypeRequest model);
        Task<DataTable> GetDocumentType(DocumentFetchRequest model);
    }

    public class DocumentTypeService : IDocumentTypeService
    {
        private readonly DocumentTypeDBHelper _sqlDBHelper;

        public DocumentTypeService(DocumentTypeDBHelper sqlDBHelper)
        {
            _sqlDBHelper = sqlDBHelper;
        }

        public async Task<int> InsertUpdateDocumentType(DocumentTypeRequest model)
        {
            return await _sqlDBHelper.InsertUpdateDocumentType(model);
        }
        public async Task<DataTable> GetDocumentType(DocumentFetchRequest model)
        {
            return await _sqlDBHelper.GetDocumentType(model);
        }
    }
}