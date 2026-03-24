using Microsoft.AspNetCore.Mvc;

namespace OCR_BACKEND.Modals
{
    public class OcrRequest
    {
        public IFormFile Image { get; set; }
    }

    public class OcrUploadRequest
    {
        [FromForm(Name = "files")]
        public List<IFormFile> Files { get; set; } = new();
    }

    public class OcrJobFetchRequest
    {
        public int StartIndex { get; set; } = 0;
        public int PageSize { get; set; } = 10;
        public string? SearchBy { get; set; }
        public string? SearchCriteria { get; set; }
    }

    public class OcrJob
    {
        public Guid JobId { get; set; }
        public string Status { get; set; } = "";
        public int TotalFiles { get; set; }
        public int ProcessedFiles { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    public class OcrJobResult
    {
        public Guid ResultId { get; set; }
        public Guid JobId { get; set; }
        public string FileName { get; set; } = "";
        public string? OcrText { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    // Internal use — passed to background worker via Channel
    public record OcrJobQueueItem(Guid JobId, List<string> FilePaths);
}
