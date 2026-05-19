namespace OCR_BACKEND.Modals
{
    public class ApproveDocumentFetchRequest
    {
        public int StartIndex { get; set; }
        public int PageSize { get; set; }
        public string? SearchBy { get; set; }
        public string? SearchCriteria { get; set; }
    }
}
