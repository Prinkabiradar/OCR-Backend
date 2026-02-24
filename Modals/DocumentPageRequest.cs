namespace OCR_BACKEND.Modals
{
    public class DocumentPageRequest
    {
        public int DocumentPageId { get; set; }
        public int DocumentId { get; set; }
        public int PageNumber { get; set; }
        public string ExtractedText { get; set; }
        public int StatusId { get; set; }
        public int CreatedBy { get; set; }
    }
}
