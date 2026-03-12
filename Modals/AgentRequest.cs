namespace OCR_BACKEND.Modals
{
    public class AgentRequest
    {
        public string Question { get; set; }
    }

 
    public class DocumentPageResult
    {
        public int DocumentPageId { get; set; }
        public int DocumentId { get; set; }
        public int PageNumber { get; set; }
        public string ExtractedText { get; set; }
        public string DocumentName { get; set; }
        public int TotalCount { get; set; }  
    }

    public class AgentResponse
    {
        public string DocumentName { get; set; }
        public List<DocumentPageResult> Pages { get; set; }
        public string FullText { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
    }
}