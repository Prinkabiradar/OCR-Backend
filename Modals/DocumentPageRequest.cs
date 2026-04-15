namespace OCR_BACKEND.Modals
{
    public class DocumentPageRequest
    {
        public int DocumentPageId { get; set; }
        public int DocumentId { get; set; }
        public int PageNumber { get; set; }
        public string ExtractedText { get; set; }
        public int StatusId { get; set; }
        public int UserId { get; set; }
        public string? RejectionReason { get; set; }
        public int RoleId { get; set; }
        public Guid job_id { get; set; }
        public string? file_name { get; set; }
    }
}
