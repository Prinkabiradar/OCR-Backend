using System.Text.Json.Serialization;

namespace OCR_BACKEND.Modals
{
    public class DashboardResponse
    {
        [JsonPropertyName("stats")]
        public DashboardStats Stats { get; set; } = new();

        [JsonPropertyName("recentDocs")]
        public List<RecentDocumentResult> RecentDocs { get; set; } = new();

        [JsonPropertyName("typeBreakdown")]
        public List<DocumentTypeBreakdown> TypeBreakdown { get; set; } = new();

        [JsonPropertyName("monthlyActivity")]
        public List<MonthlyUploadActivity> MonthlyActivity { get; set; } = new();

        [JsonPropertyName("topSearched")]
        public List<TopSearchedDocument> TopSearched { get; set; } = new();

        [JsonPropertyName("todaySeva")]
        public TodaySevaStats TodaySeva { get; set; } = new();
    }

    // ── SP1 : stats ───────────────────────────────────────────────────────────
    public class DashboardStats
    {
        [JsonPropertyName("TotalDocuments")]
        public long TotalDocuments { get; set; }

        [JsonPropertyName("TotalPagesScanned")]
        public long TotalPagesScanned { get; set; }

        [JsonPropertyName("AISummaries")]
        public long AISummaries { get; set; }

        [JsonPropertyName("DocumentTypes")]
        public long DocumentTypes { get; set; }

        [JsonPropertyName("LanguagesSupported")]
        public long LanguagesSupported { get; set; }

        [JsonPropertyName("TodayUploads")]
        public long TodayUploads { get; set; }

        [JsonPropertyName("TodaySummaries")]
        public long TodaySummaries { get; set; }

        [JsonPropertyName("ThisMonthDocuments")]
        public long ThisMonthDocuments { get; set; }

        [JsonPropertyName("ThisWeekPages")]
        public long ThisWeekPages { get; set; }

        [JsonPropertyName("TodayUploadsChange")]
        public long TodayUploadsChange { get; set; }
    }

    // ── SP2 : recentDocs ──────────────────────────────────────────────────────
    public class RecentDocumentResult
    {
        [JsonPropertyName("DocumentId")]
        public int DocumentId { get; set; }

        [JsonPropertyName("DocumentName")]
        public string DocumentName { get; set; } = string.Empty;

        [JsonPropertyName("DocumentTypeName")]
        public string DocumentTypeName { get; set; } = string.Empty;

        [JsonPropertyName("TotalPages")]
        public int TotalPages { get; set; }

        [JsonPropertyName("CreatedDate")]
        public DateTime CreatedDate { get; set; }

        [JsonPropertyName("Status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("StatusClass")]
        public string StatusClass { get; set; } = string.Empty;
    }

    // ── SP3 : typeBreakdown ───────────────────────────────────────────────────
    public class DocumentTypeBreakdown
    {
        [JsonPropertyName("DocumentTypeId")]
        public int DocumentTypeId { get; set; }

        [JsonPropertyName("DocumentTypeName")]
        public string DocumentTypeName { get; set; } = string.Empty;

        [JsonPropertyName("DocumentCount")]
        public long DocumentCount { get; set; }

        [JsonPropertyName("Percentage")]
        public decimal Percentage { get; set; }
    }

    // ── SP4 : monthlyActivity ─────────────────────────────────────────────────
    public class MonthlyUploadActivity
    {
        [JsonPropertyName("Year")]
        public int Year { get; set; }

        [JsonPropertyName("Month")]
        public int Month { get; set; }

        [JsonPropertyName("MonthName")]
        public string MonthName { get; set; } = string.Empty;

        [JsonPropertyName("DocumentCount")]
        public long DocumentCount { get; set; }
    }

    // ── SP5 : topSearched ─────────────────────────────────────────────────────
    public class TopSearchedDocument
    {
        [JsonPropertyName("Rank")]
        public int Rank { get; set; }

        [JsonPropertyName("DocumentName")]
        public string DocumentName { get; set; } = string.Empty;

        [JsonPropertyName("SearchCount")]
        public long SearchCount { get; set; }
    }

    // ── SP6 : todaySeva ───────────────────────────────────────────────────────
    public class TodaySevaStats
    {
        [JsonPropertyName("TodayUploads")]
        public long TodayUploads { get; set; }

        [JsonPropertyName("TodayPages")]
        public long TodayPages { get; set; }

        [JsonPropertyName("TodaySummaries")]
        public long TodaySummaries { get; set; }

        [JsonPropertyName("TodayNewTypes")]
        public long TodayNewTypes { get; set; }

        [JsonPropertyName("TodayVoiceReqs")]
        public long TodayVoiceReqs { get; set; }
    }
}