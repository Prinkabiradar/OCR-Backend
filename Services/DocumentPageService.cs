using OCR_BACKEND.Modals;
using System.Data;

namespace OCR_BACKEND.Services
{
    public interface IDocumentPageService
    {
        Task<int> InsertUpdateDocumentPage(DocumentPageRequest model);
        Task<DataTable> GetDocumentPagesByDocument(OcrDocumentRequest request);
        Task<DataTable> GetDocumentsByDocumentType(DocumentFetchRequest model);
        Task<DataTable> GetSuggestionPages(SuggestionPageRequest model);
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
            var before = model.ExtractedText ?? string.Empty;
            model.ExtractedText = ExtractedTextSanitizer.ToPlainBlackFriendlyText(model.ExtractedText);
            var after = model.ExtractedText ?? string.Empty;
            Console.WriteLine($"[DocumentPageService] ExtractedText before sanitize len={before.Length}, after len={after.Length}");
            Console.WriteLine($"[DocumentPageService] Sanitized preview: {after.Substring(0, Math.Min(300, after.Length))}");
            return await _sqlDBHelper.InsertUpdateDocumentPage(model);
        }
        public async Task<DataTable> GetDocumentPagesByDocument(OcrDocumentRequest request)
        {
            return await _sqlDBHelper.GetDocumentPagesByDocument(request);
        }
        public async Task<DataTable> GetDocumentsByDocumentType(DocumentFetchRequest model)
        {
            return await _sqlDBHelper.GetDocumentsByDocumentType(model);
        }
        public async Task<DataTable> GetSuggestionPages(SuggestionPageRequest request)
        {
            return await _sqlDBHelper.GetSuggestionPages(request);
        }
    }
}
