namespace OCR_BACKEND.Modals
{
    public class DocumentTypeRequest
    {
        public int DocumentTypeId { get; set; }
        public string DocumentTypeName { get; set; }
        public bool IsActive { get; set; }
        public int CreatedBy { get; set; }
    }
}
