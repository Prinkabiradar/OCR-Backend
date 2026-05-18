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

        [FromForm(Name = "geminiModel")]
        public string? GeminiModel { get; set; }
    }

    public class OcrJobFetchRequest
    {
        public int StartIndex { get; set; } = 0;
        public int PageSize { get; set; } = 10;
        public string? SearchBy { get; set; }
        public string? SearchCriteria { get; set; }
    }

    public class RetryOcrResultRequest
    {
        public Guid JobId { get; set; }
        public string FileName { get; set; } = "";
        public string? GeminiModel { get; set; }
    }

    public class CancelOcrJobRequest
    {
        public Guid JobId { get; set; }
    }

    public class VerifyOcrJobRequest
    {
        public Guid JobId { get; set; }
        public int? ExpectedTotalPages { get; set; }
    }

    public class OcrPageVerificationIssue
    {
        public string Type { get; set; } = "";
        public string Message { get; set; } = "";
        public List<int> PageNumbers { get; set; } = new();
        public List<string> Files { get; set; } = new();
    }

    public class OcrPageVerificationResult
    {
        public Guid JobId { get; set; }
        public int ExpectedTotalPages { get; set; }
        public int ProcessedResultCount { get; set; }
        public int DetectedNumberedPageCount { get; set; }
        public bool IsPageOrderValid { get; set; }
        public bool HasMissingPages { get; set; }
        public bool HasDuplicatePages { get; set; }
        public bool HasDuplicateContent { get; set; }
        public bool CanFinalize { get; set; }
        public List<int> DetectedPageOrder { get; set; } = new();
        public List<int> MissingPages { get; set; } = new();
        public List<int> DuplicatePages { get; set; } = new();
        public List<OcrPageVerificationIssue> Issues { get; set; } = new();
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
        public string? FilePath { get; set; }
    }

    // Internal use — passed to background worker via Channel
    public record OcrJobPageReference(int PageNumber, string FileName);

    public record OcrJobWorkItem(
        string FilePath,
        string OriginalSourcePath,
        List<OcrJobPageReference> Pages);

    public record OcrJobQueueItem(Guid JobId, List<OcrJobWorkItem> Items, string? GeminiModel = null);
}
