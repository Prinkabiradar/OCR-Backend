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

    public class DocumentSummaryRecord
    {
        public int SummaryId { get; set; }
        public string DocumentName { get; set; } = string.Empty;
        public string SummaryText { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }       
        public int? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public int? ApprovedBy { get; set; }
    }

    public class SaveSummaryRequest
    {
        public int SummaryId { get; set; }
        public string DocumentName { get; set; } = string.Empty;
        public string SummaryText { get; set; } = string.Empty;
        public int UserId { get; set; }
        public int RoleId { get; set; }

    }

    public class SummarizeResponse
    {
        public int SummaryId { get; set; }
        public string Summary { get; set; } = string.Empty;
        public bool FromCache { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class SummaryData
    {
        public int StartIndex { get; set; }
        public int PageSize { get; set; }
        public string? SearchBy { get; set; }
        public string? SearchCriteria { get; set; }
    }
}