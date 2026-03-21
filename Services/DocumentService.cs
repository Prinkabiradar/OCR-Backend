using OCR_BACKEND.Modals;
using System.Data;

namespace OCR_BACKEND.Services
{
    public interface IDocumentService
    {
        Task<int> InsertUpdateDocument(DocumentRequest model);
        Task<DataTable> GetDocuments(DocumentFetchRequest model);
    }

    public class DocumentService: IDocumentService
    {
        private readonly DocumentDBHelper _sqlDBHelper;

        public DocumentService(DocumentDBHelper sqlDBHelper)
        {
            _sqlDBHelper = sqlDBHelper;
        }

        public async Task<int> InsertUpdateDocument(DocumentRequest model)
        {
            return await _sqlDBHelper.InsertUpdateDocument(model);
        }
        public async Task<DataTable> GetDocuments(DocumentFetchRequest model)
        {
            return await _sqlDBHelper.GetDocuments(model);
        }
    }
}
