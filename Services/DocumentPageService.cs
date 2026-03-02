using OCR_BACKEND.Modals;
using System.Data;

namespace OCR_BACKEND.Services
{
    public interface IDocumentPageService
    {
        Task<int> InsertUpdateDocumentPage(DocumentPageRequest model);
        Task<DataTable> GetDocumentPagesByDocument(int documentId);
    }

    public class DocumentPageService: IDocumentPageService
    {
        private readonly DocumentPageDBHelper _sqlDBHelper;

        public DocumentPageService(DocumentPageDBHelper sqlDBHelper)
        {
            _sqlDBHelper = sqlDBHelper;
        }
        public async Task<int> InsertUpdateDocumentPage(DocumentPageRequest model)
        {
            return await _sqlDBHelper.InsertUpdateDocumentPage(model);
        }
        public async Task<DataTable> GetDocumentPagesByDocument(int documentId)
        {
            return await _sqlDBHelper.GetDocumentPagesByDocument(documentId);
        }
    }
}
