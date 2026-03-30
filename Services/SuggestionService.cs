using System.Data;
using OCR_BACKEND.Modals;

namespace OCR_BACKEND.Services
{
    public interface ISuggestionService
    {
        Task<int> InsertPageSuggestion(SuggestionRequest model);
        Task<DataTable> GetActiveSuggestion(DocumentFetchRequest request);
        Task<int> ReviewSuggestion(int suggestionId, int documentPageId, string action, int reviewedBy, int roleId);
    }
    public class SuggestionService : ISuggestionService
    {
        private readonly SuggestionDBHelper _dbHelper;

        public SuggestionService(SuggestionDBHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        public async Task<int> InsertPageSuggestion(SuggestionRequest model)
        {
            return await _dbHelper.InsertPageSuggestion(model);
        }

        public async Task<DataTable> GetActiveSuggestion(DocumentFetchRequest request)
        {
            return await _dbHelper.GetActiveSuggestion( request);
        }

        public async Task<int> ReviewSuggestion(int suggestionId, int documentPageId, string action, int reviewedBy, int roleId)
        {
            return await _dbHelper.ReviewSuggestion(suggestionId, documentPageId, action, reviewedBy, roleId);
        }
    }
}