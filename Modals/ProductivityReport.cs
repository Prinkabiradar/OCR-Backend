namespace OCR_BACKEND.Modals
{
    public class ProductivityReport
    {
        public DateOnly ReportDate { get; set; }
        public DateTime GeneratedAt { get; set; }
        public long TotalDocumentsProcessedToday { get; set; }
        public long TotalDocumentsProcessedOverall { get; set; }
        public long TotalPagesCompletedToday { get; set; }
        public long TotalPagesCompletedOverall { get; set; }
        public List<UserProductivitySummary> Users { get; set; } = new();
    }

    public class UserProductivitySummary
    {
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Mobile { get; set; }
        public DateTime? FirstLoginTime { get; set; }
        public DateTime? LastLogoutTime { get; set; }
        public long DocumentsProcessedToday { get; set; }
        public long DocumentsProcessedOverall { get; set; }
        public long PagesCompletedToday { get; set; }
        public long PagesCompletedOverall { get; set; }
    }
}
