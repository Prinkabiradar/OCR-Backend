using OCR_BACKEND.Modals;

namespace OCR_BACKEND.Services
{
    public interface IDocumentPageService
    {
        Task<int> InsertUpdateDocumentPage(DocumentPageRequest model);
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
    }
}
