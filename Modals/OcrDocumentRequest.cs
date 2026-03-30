namespace OCR_BACKEND.Modals
{
    public class OcrDocumentRequest
    {
        public int DocumentId { get; set; }

        public int StartIndex { get; set; }

        public int PageSize { get; set; }

        public string? SearchBy { get; set; }

        public string? SearchCriteria { get; set; }
        public int RoleId { get; set; }
    }

    public class SuggestionPageRequest
    {
        public int DocumentId { get; set; }
        public int DocumentPageId { get; set; }

        public int StartIndex { get; set; }

        public int PageSize { get; set; }

        public string? SearchBy { get; set; }

        public string? SearchCriteria { get; set; }
        public int RoleId { get; set; }
    }
}
