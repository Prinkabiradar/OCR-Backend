namespace OCR_BACKEND.Modals
{
    public class PaginationRequest
    {
        public int UserId { get; set; }
        public string StartIndex { get; set; }
        public string PageSize { get; set; }
        public string SearchBy { get; set; }
        public string? SearchCriteria { get; set; }
    }
}
