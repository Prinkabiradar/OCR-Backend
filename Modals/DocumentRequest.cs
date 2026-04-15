namespace OCR_BACKEND.Modals
{
    public class DocumentRequest
    {
        public int DocumentId { get; set; }
        public int DocumentTypeId { get; set; }
        public string DocumentName { get; set; }
        public int TotalPages { get; set; }
        public int CreatedBy { get; set; }
    }
}
