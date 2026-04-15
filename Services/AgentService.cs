using OCR_BACKEND.Modals;
using System.Data;
using System.Text.Json;

namespace OCR_BACKEND.Services
{
    public interface IAgentService
    {
        
        Task<AgentResponse> Ask(string question, int startIndex, int pageSize);
        Task<SummarizeResponse> Summarize(string documentName);
        Task<DataTable> GetSummaryData(SummaryData model);
        Task<SummarizeResponse> SaveSummary(string documentName, string summaryText,int summaryId,int userId, int roleId);
    }

    public class AgentService : IAgentService
    {
        private readonly AgentDBHelper _agentDBHelper;
        private readonly IConfiguration _config;
        private readonly HttpClient _httpClient;

        public AgentService(
            AgentDBHelper agentDBHelper,
            IConfiguration config,
            HttpClient httpClient)
        {
            _agentDBHelper = agentDBHelper;
            _config = config;
            _httpClient = httpClient;
        }

         
        public async Task<AgentResponse> Ask(
            string question,
            int startIndex,
            int pageSize)
        {
            var keyword = question;    //ExtractKeyword(question);

            var (pages, totalCount) = await _agentDBHelper.SearchDocumentPages(
                keyword, startIndex, pageSize
            );

            if (pages.Count == 0)
                return new AgentResponse
                {
                    DocumentName = keyword,
                    Pages = new List<DocumentPageResult>(),
                    FullText = $"Sorry, no document found with name '{keyword}'.",
                    TotalCount = 0,
                    TotalPages = 0,
                    CurrentPage = 1,
                    PageSize = pageSize
                };

            var fullText = string.Join("\n\n",
                pages.Select(p => $"Page {p.PageNumber}:\n{p.ExtractedText}")
            );

            return new AgentResponse
            {
                DocumentName = pages.First().DocumentName,
                Pages = pages,
                FullText = fullText,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                CurrentPage = (startIndex / pageSize) + 1,
                PageSize = pageSize
            };
        }


        //public async Task<string> Summarize(string documentName)
        //{

        //    var pages = await _agentDBHelper.SearchDocumentPages(documentName);

        //    if (pages.Count == 0)
        //        return $"No document found with name '{documentName}'.";

        //    var fullText = string.Join("\n\n",
        //        pages.Select(p => $"Page {p.PageNumber}:\n{p.ExtractedText}")
        //    );

        //    var apiKey = _config["Gemini:ApiKey"];
        //    var url = $"https://generativelanguage.googleapis.com/v1/models/gemini-2.5-flash:generateContent?key={apiKey}";

        //    var prompt = $@"
        //        You are a document summarizer.
        //        Below is the full text of a document called '{documentName}'.
        //        Please provide:
        //        1. A brief summary (3-5 sentences)
        //        2. Key points (bullet points)
        //        3. Important names or dates mentioned
        //        4. Overall theme or purpose of the document

        //        DOCUMENT TEXT:
        //        {fullText}
        //    ";

        //    var requestBody = new
        //    {
        //        contents = new[]
        //        {
        //            new { parts = new[] { new { text = prompt } } }
        //        }
        //    };

        //    var response = await _httpClient.PostAsJsonAsync(url, requestBody);
        //    var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        //    return json
        //        .GetProperty("candidates")[0]
        //        .GetProperty("content")
        //        .GetProperty("parts")[0]
        //        .GetProperty("text")
        //        .GetString() ?? "Summary could not be generated.";
        //}


        public async Task<SummarizeResponse> Summarize(string documentName )
        {
            // 1. Check DB first
            var saved = await _agentDBHelper.GetDocumentSummary(documentName);
            if (saved != null)
            {
                return new SummarizeResponse
                {
                    SummaryId = saved.SummaryId,
                    Summary = saved.SummaryText,
                    FromCache = true,
                    UpdatedAt = saved.UpdatedAt
                };
            }

            // 2. Not saved — fetch pages and call Gemini
            var pages = await _agentDBHelper.SearchDocumentPages(documentName);
            if (pages.Count == 0)
                return new SummarizeResponse
                {
                    Summary = $"No document found with name '{documentName}'.",
                    FromCache = false
                };

            var fullText = string.Join("\n\n",
                pages.Select(p => $"Page {p.PageNumber}:\n{p.ExtractedText}"));
            //gemini-3.0-pro
            var apiKey = _config["Gemini:ApiKey"];
           // var url = $"https://generativelanguage.googleapis.com/v1/models/gemini-2.5-flash:generateContent?key={apiKey}";
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3-flash-preview:generateContent?key={apiKey}";

            var prompt = $@"
                        You are a document summarizer.
                        Below is the full text of a document called '{documentName}'.
                        Instructions:
                        - Detect the language of the document text.
                        - Generate the summary in the SAME language as the document.
                        - Do NOT translate the output into another language.
                        Please provide:
                        1. A brief summary (3-5 sentences)
                        2. Key points (bullet points)
                        3. Important names or dates mentioned
                        4. Overall theme or purpose of the document

                        DOCUMENT TEXT:
                        {fullText}
                    ";

            var requestBody = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } }
            };

            var response = await _httpClient.PostAsJsonAsync(url, requestBody);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var geminiText = json
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "Summary could not be generated.";

            // ✅ No auto-save here — just return Gemini result
            return new SummarizeResponse
            {
                SummaryId = 0,           // 0 means not saved yet
                Summary = geminiText,
                FromCache = false,
                UpdatedAt = null
            };
        }
        private string ExtractKeyword(string question)
        {
            var stopWords = new[] {
                "i", "want", "to", "listen", "read", "me", "the",
                "poem", "document", "letter", "please", "can", "you",
                "a", "an", "of", "recite", "speak", "tell", "about",
                "hear", "play", "show", "get", "find", "search"
            };

            var words = question
                .ToLower()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !stopWords.Contains(w))
                .ToList();

            return words.Any() ? string.Join(" ", words) : question;
        }

        public async Task<SummarizeResponse> SaveSummary(
        string documentName, string summaryText, int summaryId, int userId , int roleId)
        {
            int returnedId = await _agentDBHelper.InsertUpdateDocumentSummary(
                summaryId: summaryId,
                documentName: documentName,
                summaryText: summaryText,
                userId: userId  ,
                roleId:roleId
            );

            var record = await _agentDBHelper.GetDocumentSummary(documentName);

            return new SummarizeResponse
            {
                SummaryId = returnedId,
                Summary = record!.SummaryText,
                FromCache = true,
                UpdatedAt = record.UpdatedAt
            };
        }

        public async Task<DataTable> GetSummaryData(SummaryData model)
       {
            return await _agentDBHelper.GetSummaryData(model);
        }
    }
}