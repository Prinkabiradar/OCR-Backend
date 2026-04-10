using OCR_BACKEND.Modals;
using OCR_BACKEND.Queue;
using OCR_BACKEND.Services;
using System.Text.Json;
using System.Threading.Channels;

namespace OCR_BACKEND.BackgroundServices
{
    public class OcrWorkerService : BackgroundService
    {
        private readonly OcrJobQueue _ocrJobQueue;
        private readonly OcrJobDBHelper _ocrJobDBHelper;
        private readonly GeminiService _gemini;
        private readonly OcrJobCancellationRegistry _cancellationRegistry;
        private readonly ILogger<OcrWorkerService> _logger;
        private readonly IConfiguration _config;

        public OcrWorkerService(
            OcrJobQueue ocrJobQueue,
            OcrJobDBHelper ocrJobDBHelper,
            GeminiService gemini,
            OcrJobCancellationRegistry cancellationRegistry,
            ILogger<OcrWorkerService> logger,
            IConfiguration config)
        {
            _ocrJobQueue = ocrJobQueue;
            _ocrJobDBHelper = ocrJobDBHelper;
            _gemini = gemini;
            _cancellationRegistry = cancellationRegistry;
            _logger = logger;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var item in _ocrJobQueue.ReadAllAsync(stoppingToken))
            {
                _ = Task.Run(async () =>
                {
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        stoppingToken,
                        _cancellationRegistry.GetToken(item.JobId));

                    try
                    {
                        await ProcessJobAsync(item, linkedCts.Token);
                    }
                    finally
                    {
                        _cancellationRegistry.Release(item.JobId);
                    }
                }, stoppingToken);
            }
        }

        private async Task ProcessJobAsync(OcrJobQueueItem item, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var totalPages = item.Items.Sum(GetWorkItemPageCount);

            _logger.LogInformation(
                "Job {JobId} started with {ChunkCount} work item(s) covering {PageCount} page(s)",
                item.JobId,
                item.Items.Count,
                totalPages);

            await _ocrJobDBHelper.UpdateJobStatus(item.JobId, "Processing", 0);

            try
            {
                var storageRoot = Path.GetFullPath(_config["FileStorage:Root"] ?? "uploads");
                var insertBatchSize = Math.Max(1, _config.GetValue("Ocr:InsertBatchSize", 10));
                var resultChannel = Channel.CreateUnbounded<List<OcrJobResult>>();

                var producer = Task.Run(async () =>
                {
                    try
                    {
                        var launchIndex = 0;
                        var options = new ParallelOptions
                        {
                            MaxDegreeOfParallelism = Math.Max(1, _config.GetValue("Ocr:MaxParallelChunks", 5)),
                            CancellationToken = ct
                        };

                        await Parallel.ForEachAsync(item.Items, options, async (workItem, token) =>
                        {
                            var staggerDelayMs = Math.Max(0, _config.GetValue("Ocr:StaggerDelayMs", 200));
                            var currentIndex = Interlocked.Increment(ref launchIndex);
                            if (staggerDelayMs > 0 && currentIndex > 1)
                                await Task.Delay(staggerDelayMs, token);

                            var results = await ProcessWorkItemWithRetryAsync(
                                item.JobId,
                                workItem,
                                storageRoot,
                                token);

                            await resultChannel.Writer.WriteAsync(results, token);
                        });

                        resultChannel.Writer.TryComplete();
                    }
                    catch (Exception ex)
                    {
                        resultChannel.Writer.TryComplete(ex);
                    }
                }, ct);

                var processedCount = 0;
                var batch = new List<OcrJobResult>(insertBatchSize);

                await foreach (var resultSet in resultChannel.Reader.ReadAllAsync(ct))
                {
                    batch.AddRange(resultSet);
                    processedCount += resultSet.Count;

                    if (batch.Count >= insertBatchSize)
                    {
                        await _ocrJobDBHelper.BulkInsertJobResults(batch);
                        batch.Clear();
                    }

                    await _ocrJobDBHelper.UpdateJobStatus(item.JobId, "Processing", processedCount);

                    _logger.LogInformation(
                        "Job {JobId} progress: {Done}/{Total} page(s)",
                        item.JobId,
                        processedCount,
                        totalPages);
                }

                await producer;

                if (batch.Count > 0)
                    await _ocrJobDBHelper.BulkInsertJobResults(batch);

                await _ocrJobDBHelper.UpdateJobStatus(item.JobId, "Completed", processedCount);

                CleanupConvertedDirectory(item);

                _logger.LogInformation("Job {JobId} completed", item.JobId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Job {JobId} cancelled", item.JobId);
                await _ocrJobDBHelper.UpdateJobStatus(item.JobId, "Failed", 0, "Job was cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", item.JobId);
                await _ocrJobDBHelper.UpdateJobStatus(item.JobId, "Failed", 0, ex.Message);
            }
        }

        private async Task<List<OcrJobResult>> ProcessWorkItemWithRetryAsync(
            Guid jobId,
            OcrJobWorkItem workItem,
            string storageRoot,
            CancellationToken ct)
        {
            var fileName = Path.GetFileName(workItem.FilePath);
            var maxRetries = Math.Max(1, _config.GetValue("Ocr:MaxRetries", 7));
            var retryBaseDelayMs = Math.Max(250, _config.GetValue("Ocr:RetryBaseDelayMs", 5000));
            var maxRetryDelayMs = Math.Max(retryBaseDelayMs, _config.GetValue("Ocr:MaxRetryDelayMs", 60_000));

            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(workItem.FilePath, ct);
                    var contentType = ResolveContentType(workItem.FilePath);
                    var ocrText = await _gemini.ExtractTextFromFileBytes(bytes, contentType);

                    if (IsRetryableResponse(ocrText))
                    {
                        _logger.LogWarning(
                            "Work item {File} got a retryable Gemini response (attempt {Attempt}/{Max})",
                            fileName,
                            attempt,
                            maxRetries);

                        if (attempt == maxRetries)
                            break;

                        var delay = GetRetryDelayMs(attempt, retryBaseDelayMs, maxRetryDelayMs);
                        await Task.Delay(delay, ct);
                        continue;
                    }

                    return await BuildSuccessResultsAsync(jobId, workItem, storageRoot, ocrText, ct);
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    _logger.LogWarning(
                        "Work item {File} failed on attempt {Attempt}/{Max}: {Message}",
                        fileName,
                        attempt,
                        maxRetries,
                        ex.Message);

                    var delay = GetRetryDelayMs(attempt, retryBaseDelayMs, maxRetryDelayMs);
                    await Task.Delay(delay, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        "Work item {File} failed after {Max} attempts: {Message}",
                        fileName,
                        maxRetries,
                        ex.Message);

                    return BuildFailureResults(jobId, workItem, storageRoot, ex.Message);
                }
            }

            return BuildFailureResults(
                jobId,
                workItem,
                storageRoot,
                "Gemini returned retryable errors after all retry attempts.");
        }

        private async Task<List<OcrJobResult>> BuildSuccessResultsAsync(
            Guid jobId,
            OcrJobWorkItem workItem,
            string storageRoot,
            string rawResponse,
            CancellationToken ct)
        {
            var relativePath = Path.GetRelativePath(
                storageRoot,
                Path.GetFullPath(workItem.OriginalSourcePath));

            if (GetWorkItemPageCount(workItem) == 1)
            {
                var page = workItem.Pages.FirstOrDefault()
                    ?? new OcrJobPageReference(1, Path.GetFileName(workItem.OriginalSourcePath));
                var normalizedResponse = NormalizeSinglePageResponse(rawResponse);

                return new List<OcrJobResult>
                {
                    new()
                    {
                        JobId = jobId,
                        FileName = page.FileName,
                        OcrText = normalizedResponse,
                        Success = true,
                        FilePath = relativePath
                    }
                };
            }

            if (!TryParseMultiPageGeminiResponse(rawResponse, out var pagePayloads, out var parseError))
            {
                _logger.LogWarning(
                    "Could not parse multi-page Gemini response for {File}: {Error}. Falling back to single-page OCR.",
                    workItem.FilePath,
                    parseError);

                return await ReprocessPagesIndividuallyAsync(jobId, workItem, storageRoot, ct);
            }

            var payloadByPage = new Dictionary<int, JsonElement>();
            for (var i = 0; i < pagePayloads.Count; i++)
            {
                var payload = pagePayloads[i];
                if (payload.TryGetProperty("page", out var pageProperty) &&
                    pageProperty.ValueKind == JsonValueKind.Number &&
                    pageProperty.TryGetInt32(out var parsedPageNumber))
                {
                    payloadByPage[parsedPageNumber] = payload;
                }
            }

            var results = new List<OcrJobResult>(workItem.Pages.Count);
            for (var i = 0; i < workItem.Pages.Count; i++)
            {
                var page = workItem.Pages[i];
                JsonElement payload;

                if (!payloadByPage.TryGetValue(page.PageNumber, out payload))
                {
                    if (i < pagePayloads.Count)
                        payload = pagePayloads[i];
                    else
                    {
                        results.Add(new OcrJobResult
                        {
                            JobId = jobId,
                            FileName = page.FileName,
                            Success = false,
                            Error = $"Gemini did not return OCR output for page {page.PageNumber}.",
                            FilePath = relativePath
                        });
                        continue;
                    }
                }

                results.Add(new OcrJobResult
                {
                    JobId = jobId,
                    FileName = page.FileName,
                    OcrText = WrapPayloadAsGeminiResponse(payload),
                    Success = true,
                    FilePath = relativePath
                });
            }

            return results;
        }

        private async Task<List<OcrJobResult>> ReprocessPagesIndividuallyAsync(
            Guid jobId,
            OcrJobWorkItem workItem,
            string storageRoot,
            CancellationToken ct)
        {
            var relativePath = Path.GetRelativePath(
                storageRoot,
                Path.GetFullPath(workItem.OriginalSourcePath));

            var results = new List<OcrJobResult>(workItem.Pages.Count);

            for (var index = 0; index < workItem.Pages.Count; index++)
            {
                var page = workItem.Pages[index];
                var pageLabel = $"{Path.GetFileName(workItem.FilePath)} page {page.PageNumber}";
                var pageBytes = ExtractSinglePagePdfBytes(workItem.FilePath, index + 1);
                var pageResponse = await ExecuteGeminiWithRetryAsync(
                    () => _gemini.ExtractTextFromFileBytes(pageBytes, "application/pdf"),
                    pageLabel,
                    ct);

                if (pageResponse is null)
                {
                    results.Add(new OcrJobResult
                    {
                        JobId = jobId,
                        FileName = page.FileName,
                        Success = false,
                        Error = "Gemini returned retryable errors after all retry attempts.",
                        FilePath = relativePath
                    });
                    continue;
                }

                results.Add(new OcrJobResult
                {
                    JobId = jobId,
                    FileName = page.FileName,
                    OcrText = NormalizeSinglePageResponse(pageResponse),
                    Success = true,
                    FilePath = relativePath
                });
            }

            return results;
        }

        private List<OcrJobResult> BuildFailureResults(
            Guid jobId,
            OcrJobWorkItem workItem,
            string storageRoot,
            string error)
        {
            var relativePath = Path.GetRelativePath(
                storageRoot,
                Path.GetFullPath(workItem.OriginalSourcePath));

            if (workItem.Pages.Count == 0)
            {
                return new List<OcrJobResult>
                {
                    new()
                    {
                        JobId = jobId,
                        FileName = Path.GetFileName(workItem.OriginalSourcePath),
                        Success = false,
                        Error = error,
                        FilePath = relativePath
                    }
                };
            }

            return workItem.Pages
                .Select(page => new OcrJobResult
                {
                    JobId = jobId,
                    FileName = page.FileName,
                    Success = false,
                    Error = error,
                    FilePath = relativePath
                })
                .ToList();
        }

        private void CleanupConvertedDirectory(OcrJobQueueItem item)
        {
            try
            {
                var firstWorkItem = item.Items.FirstOrDefault();
                if (firstWorkItem is null)
                    return;

                var dir = Path.GetDirectoryName(firstWorkItem.FilePath);
                if (dir is null)
                    return;

                if (Path.GetFileName(dir).Equals("converted", StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                    _logger.LogInformation("Cleaned up converted/ for job {JobId}", item.JobId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Cleanup failed for job {JobId}: {Message}", item.JobId, ex.Message);
            }
        }

        private static int GetWorkItemPageCount(OcrJobWorkItem item)
            => item.Pages.Count == 0 ? 1 : item.Pages.Count;

        private async Task<string?> ExecuteGeminiWithRetryAsync(
            Func<Task<string>> operation,
            string fileName,
            CancellationToken ct)
        {
            var maxRetries = Math.Max(1, _config.GetValue("Ocr:MaxRetries", 7));
            var retryBaseDelayMs = Math.Max(250, _config.GetValue("Ocr:RetryBaseDelayMs", 5000));
            var maxRetryDelayMs = Math.Max(retryBaseDelayMs, _config.GetValue("Ocr:MaxRetryDelayMs", 60_000));

            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var response = await operation();
                    if (IsRetryableResponse(response))
                    {
                        _logger.LogWarning(
                            "Work item {File} got a retryable Gemini response (attempt {Attempt}/{Max})",
                            fileName,
                            attempt,
                            maxRetries);

                        if (attempt == maxRetries)
                            return null;

                        await Task.Delay(GetRetryDelayMs(attempt, retryBaseDelayMs, maxRetryDelayMs), ct);
                        continue;
                    }

                    return response;
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    _logger.LogWarning(
                        "Work item {File} failed on attempt {Attempt}/{Max}: {Message}",
                        fileName,
                        attempt,
                        maxRetries,
                        ex.Message);

                    await Task.Delay(GetRetryDelayMs(attempt, retryBaseDelayMs, maxRetryDelayMs), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        "Work item {File} failed after {Max} attempts: {Message}",
                        fileName,
                        maxRetries,
                        ex.Message);
                    return null;
                }
            }

            return null;
        }

        private static int GetRetryDelayMs(int attempt, int retryBaseDelayMs, int maxRetryDelayMs)
        {
            var exponential = retryBaseDelayMs * (int)Math.Pow(2, attempt - 1);
            var jitter = Random.Shared.Next(0, 2000);
            return Math.Min(exponential + jitter, maxRetryDelayMs);
        }

        private static byte[] ExtractSinglePagePdfBytes(string pdfPath, int pageNumber)
        {
            using var output = new MemoryStream();
            using var reader = new iText.Kernel.Pdf.PdfReader(pdfPath);
            using var writer = new iText.Kernel.Pdf.PdfWriter(output);
            using var source = new iText.Kernel.Pdf.PdfDocument(reader);
            using var target = new iText.Kernel.Pdf.PdfDocument(writer);

            source.CopyPagesTo(new List<int> { pageNumber }, target);
            target.Close();

            return output.ToArray();
        }

        private static bool TryParseMultiPageGeminiResponse(
            string rawResponse,
            out List<JsonElement> pagePayloads,
            out string? error)
        {
            pagePayloads = new List<JsonElement>();
            error = null;

            try
            {
                using var responseDocument = JsonDocument.Parse(rawResponse);
                if (!responseDocument.RootElement.TryGetProperty("candidates", out var candidates) ||
                    candidates.ValueKind != JsonValueKind.Array ||
                    candidates.GetArrayLength() == 0)
                {
                    error = "Gemini response did not contain candidates.";
                    return false;
                }

                var parts = candidates[0].GetProperty("content").GetProperty("parts");
                var textPart = parts.EnumerateArray()
                    .FirstOrDefault(part => part.TryGetProperty("text", out _));

                if (textPart.ValueKind != JsonValueKind.Object ||
                    !textPart.TryGetProperty("text", out var textElement))
                {
                    error = "Gemini response did not contain OCR text.";
                    return false;
                }

                var candidateText = StripJsonCodeFences(textElement.GetString() ?? string.Empty);
                using var payloadDocument = JsonDocument.Parse(candidateText);
                if (payloadDocument.RootElement.ValueKind != JsonValueKind.Array)
                {
                    error = "Expected a JSON array for multi-page OCR output.";
                    return false;
                }

                pagePayloads = payloadDocument.RootElement
                    .EnumerateArray()
                    .Select(element => element.Clone())
                    .ToList();

                if (pagePayloads.Count == 0)
                {
                    error = "Gemini returned an empty page array.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string StripJsonCodeFences(string value)
        {
            var trimmed = value.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
                return trimmed;

            var lines = trimmed.Split('\n').ToList();
            if (lines.Count > 0 && lines[0].StartsWith("```", StringComparison.Ordinal))
                lines.RemoveAt(0);
            if (lines.Count > 0 && lines[^1].Trim().Equals("```", StringComparison.Ordinal))
                lines.RemoveAt(lines.Count - 1);

            return string.Join('\n', lines).Trim();
        }

        private static string NormalizeSinglePageResponse(string rawResponse)
        {
            if (!TryParseMultiPageGeminiResponse(rawResponse, out var pagePayloads, out _) || pagePayloads.Count == 0)
                return rawResponse;

            return WrapPayloadAsGeminiResponse(pagePayloads[0]);
        }

        private static string WrapPayloadAsGeminiResponse(JsonElement payload)
        {
            var structured = payload.GetRawText();

            return JsonSerializer.Serialize(new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new[]
                            {
                                new
                                {
                                    text = structured
                                }
                            }
                        }
                    }
                }
            });
        }

        private static bool IsRetryableResponse(string json) =>
            json.Contains("\"code\": 503", StringComparison.Ordinal) ||
            json.Contains("\"code\":503", StringComparison.Ordinal) ||
            json.Contains("\"code\": 429", StringComparison.Ordinal) ||
            json.Contains("\"code\":429", StringComparison.Ordinal) ||
            json.Contains("overloaded", StringComparison.OrdinalIgnoreCase) ||
            json.Contains("RESOURCE_EXHAUSTED", StringComparison.Ordinal) ||
            json.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

        private static string ResolveContentType(string path) =>
            Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
    }
}
