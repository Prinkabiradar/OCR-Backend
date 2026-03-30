namespace OCR_BACKEND.Modals
{
    public class DocRequest
    {
        public int StartIndex { get; set; }
        public int PageSize { get; set; }
        public string? SearchBy { get; set; }
        public string? SearchCriteria { get; set; }
        public int RoleId { get; set; }
    }
}
