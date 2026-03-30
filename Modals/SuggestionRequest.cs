namespace OCR_BACKEND.Modals
{
    public class SuggestionRequest
    {
        public int SuggestionId { get; set; }
        public int DocumentId { get; set; }
        public int PageNumber { get; set; }
        public int DocumentPageId { get; set; }
        public string SuggestionText { get; set; } = "";
        public bool IsActive { get; set; }
        public int CreatedBy { get; set; }
        public string CreatorName { get; set; } = "";
        public DateTime CreatedDate { get; set; }
    }
}
