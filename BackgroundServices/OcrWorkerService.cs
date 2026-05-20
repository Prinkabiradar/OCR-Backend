using OCR_BACKEND.Modals;
using OCR_BACKEND.Queue;
using OCR_BACKEND.Services;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace OCR_BACKEND.BackgroundServices
{
    public class OcrWorkerService : BackgroundService
    {
        private sealed record GeminiAttemptResult(string? Response, string? ErrorDetail);

        private readonly OcrJobQueue _ocrJobQueue;
        private readonly OcrJobDBHelper _ocrJobDBHelper;
        private readonly GeminiService _gemini;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly OcrJobCancellationRegistry _cancellationRegistry;
        private readonly ILogger<OcrWorkerService> _logger;
        private readonly IConfiguration _config;

        public OcrWorkerService(
            OcrJobQueue ocrJobQueue,
            OcrJobDBHelper ocrJobDBHelper,
            GeminiService gemini,
            IServiceScopeFactory scopeFactory,
            OcrJobCancellationRegistry cancellationRegistry,
            ILogger<OcrWorkerService> logger,
            IConfiguration config)
        {
            _ocrJobQueue = ocrJobQueue;
            _ocrJobDBHelper = ocrJobDBHelper;
            _gemini = gemini;
            _scopeFactory = scopeFactory;
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
                var insertBatchSize = Math.Max(1, _config.GetValue("Ocr:InsertBatchSize", 25));
                var resultChannel = Channel.CreateUnbounded<List<OcrJobResult>>();

                var producer = Task.Run(async () =>
                {
                    try
                    {
                        var launchIndex = 0;
                        var options = new ParallelOptions
                        {
                            MaxDegreeOfParallelism = Math.Max(1, _config.GetValue("Ocr:MaxParallelChunks", 8)),
                            CancellationToken = ct
                        };

                        await Parallel.ForEachAsync(item.Items, options, async (workItem, token) =>
                        {
                            var staggerDelayMs = Math.Max(0, _config.GetValue("Ocr:StaggerDelayMs", 0));
                            var currentIndex = Interlocked.Increment(ref launchIndex);
                            if (staggerDelayMs > 0 && currentIndex > 1)
                                await Task.Delay(staggerDelayMs, token);

                            var results = await ProcessWorkItemWithRetryAsync(
                                item.JobId,
                                workItem,
                                storageRoot,
                                item.GeminiModel,
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
            string? geminiModel,
            CancellationToken ct)
        {
            var fileName = Path.GetFileName(workItem.FilePath);
            var bytes = await ReadWorkItemBytesAsync(jobId, workItem.FilePath, ct);
            var contentType = ResolveContentType(workItem.FilePath);
            var attemptResult = await ExecuteGeminiWithRetryAsync(
                () => _gemini.ExtractTextFromFileBytes(bytes, contentType, geminiModel),
                fileName,
                ct);

            if (attemptResult.Response is null)
            {
                return BuildFailureResults(
                    jobId,
                    workItem,
                    storageRoot,
                    attemptResult.ErrorDetail ?? "Gemini returned retryable errors after all retry attempts.");
            }

            return await BuildSuccessResultsAsync(jobId, workItem, storageRoot, attemptResult.Response, geminiModel, ct);
        }

        private async Task<List<OcrJobResult>> BuildSuccessResultsAsync(
            Guid jobId,
            OcrJobWorkItem workItem,
            string storageRoot,
            string rawResponse,
            string? geminiModel,
            CancellationToken ct)
        {
            var relativePath = NormalizeStoredPath(workItem.OriginalSourcePath);

            if (GetWorkItemPageCount(workItem) == 1)
            {
                var page = workItem.Pages.FirstOrDefault()
                    ?? new OcrJobPageReference(1, Path.GetFileName(workItem.OriginalSourcePath));
                var normalizedResponse = NormalizeSinglePageResponse(rawResponse);
                var extractedText = TryExtractGeminiExtractedText(normalizedResponse);

                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    _logger.LogWarning(
                        "Empty OCR text detected for {File}. Retrying single-page extraction.",
                        page.FileName);

                    return await ReprocessPagesIndividuallyAsync(
                        jobId,
                        new OcrJobWorkItem(
                            workItem.FilePath,
                            workItem.OriginalSourcePath,
                            new List<OcrJobPageReference> { page }),
                        storageRoot,
                        geminiModel,
                        ct);
                }

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

                return await ReprocessPagesIndividuallyAsync(jobId, workItem, storageRoot, geminiModel, ct);
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
            string? geminiModel,
            CancellationToken ct)
        {
            var relativePath = NormalizeStoredPath(workItem.OriginalSourcePath);

            var results = new List<OcrJobResult>(workItem.Pages.Count);

            for (var index = 0; index < workItem.Pages.Count; index++)
            {
                var page = workItem.Pages[index];
                var pageLabel = $"{Path.GetFileName(workItem.FilePath)} page {page.PageNumber}";
                var pageBytes = await ExtractSinglePagePdfBytesFromWorkItemAsync(jobId, workItem.FilePath, index + 1, ct);
                var pageResult = await ExecuteGeminiWithRetryAsync(
                    () => _gemini.ExtractTextFromFileBytes(pageBytes, "application/pdf", geminiModel),
                    pageLabel,
                    ct);

                if (pageResult.Response is null)
                {
                    results.Add(new OcrJobResult
                    {
                        JobId = jobId,
                        FileName = page.FileName,
                        Success = false,
                        Error = pageResult.ErrorDetail ?? "Gemini returned retryable errors after all retry attempts.",
                        FilePath = relativePath
                    });
                    continue;
                }

                results.Add(new OcrJobResult
                {
                    JobId = jobId,
                    FileName = page.FileName,
                    OcrText = NormalizeSinglePageResponse(pageResult.Response),
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
            var relativePath = NormalizeStoredPath(workItem.OriginalSourcePath);

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

        private static string NormalizeStoredPath(string path)
            => path.Replace("\\", "/");

        private async Task<byte[]> ReadWorkItemBytesAsync(Guid jobId, string filePath, CancellationToken ct)
        {
            if (File.Exists(filePath))
                return await File.ReadAllBytesAsync(filePath, ct);

            var parsed = TryResolveStoragePath(filePath);
            if (parsed is null)
                throw new FileNotFoundException($"Unable to resolve work item path '{filePath}' from local or storage.");

            using var scope = _scopeFactory.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var storageBytes = await storage.GetFileAsyncBytes(
                jobId.ToString(),
                parsed.Value.FileType,
                parsed.Value.FileName,
                ct);
            if (storageBytes == null || storageBytes.Length == 0)
                throw new FileNotFoundException($"Unable to read work item '{filePath}' from storage.");

            return storageBytes;
        }

        private static (string FileType, string FileName)? TryResolveStoragePath(string filePath)
        {
            var normalized = filePath.Replace("\\", "/");
            var parts = normalized
                .Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
                return null;

            var originalsIndex = Array.FindIndex(
                parts,
                p => p.Equals("originals", StringComparison.OrdinalIgnoreCase));
            if (originalsIndex >= 0 && originalsIndex < parts.Length - 1)
                return ("originals", parts[^1]);

            var convertedIndex = Array.FindIndex(
                parts,
                p => p.Equals("converted", StringComparison.OrdinalIgnoreCase));
            if (convertedIndex >= 0 && convertedIndex < parts.Length - 1)
                return ("converted", parts[^1]);

            if (parts.Length >= 3 &&
                parts[0].Equals("ocr-jobs", StringComparison.OrdinalIgnoreCase) &&
                (parts[2].Equals("originals", StringComparison.OrdinalIgnoreCase) ||
                 parts[2].Equals("converted", StringComparison.OrdinalIgnoreCase)) &&
                parts.Length >= 4)
            {
                return (parts[2], parts[^1]);
            }

            return (parts[0], parts[^1]);
        }

        private async Task<byte[]> ExtractSinglePagePdfBytesFromWorkItemAsync(
            Guid jobId,
            string filePath,
            int pageNumber,
            CancellationToken ct)
        {
            if (File.Exists(filePath))
                return ExtractSinglePagePdfBytes(filePath, pageNumber);

            var pdfBytes = await ReadWorkItemBytesAsync(jobId, filePath, ct);
            return ExtractSinglePagePdfBytes(pdfBytes, pageNumber);
        }

        private async Task<GeminiAttemptResult> ExecuteGeminiWithRetryAsync(
            Func<Task<string>> operation,
            string fileName,
            CancellationToken ct)
        {
            var maxRetries = Math.Max(1, _config.GetValue("Ocr:MaxRetries", 7));
            var retryBaseDelayMs = Math.Max(250, _config.GetValue("Ocr:RetryBaseDelayMs", 1500));
            var maxRetryDelayMs = Math.Max(retryBaseDelayMs, _config.GetValue("Ocr:MaxRetryDelayMs", 15_000));
            string? lastErrorDetail = null;

            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var response = await operation();
                    if (IsRetryableResponse(response))
                    {
                        lastErrorDetail = ExtractGeminiErrorDetail(response);
                        _logger.LogWarning(
                            "Work item {File} got a retryable Gemini response (attempt {Attempt}/{Max}): {Error}",
                            fileName,
                            attempt,
                            maxRetries,
                            lastErrorDetail ?? "Retryable Gemini error");

                        if (attempt == maxRetries)
                            return new GeminiAttemptResult(null, lastErrorDetail);

                        await Task.Delay(GetRetryDelayMs(attempt, retryBaseDelayMs, maxRetryDelayMs), ct);
                        continue;
                    }

                    return new GeminiAttemptResult(response, null);
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    lastErrorDetail = ex.Message;
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
                    lastErrorDetail = ex.Message;
                    _logger.LogError(
                        "Work item {File} failed after {Max} attempts: {Message}",
                        fileName,
                        maxRetries,
                        ex.Message);
                    return new GeminiAttemptResult(null, lastErrorDetail);
                }
            }

            return new GeminiAttemptResult(null, lastErrorDetail);
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

        private static byte[] ExtractSinglePagePdfBytes(byte[] pdfBytes, int pageNumber)
        {
            using var output = new MemoryStream();
            using var input = new MemoryStream(pdfBytes);
            using var reader = new iText.Kernel.Pdf.PdfReader(input);
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

        private static string? ExtractGeminiErrorDetail(string rawResponse)
        {
            try
            {
                using var document = JsonDocument.Parse(rawResponse);
                if (!document.RootElement.TryGetProperty("error", out var error))
                    return null;

                var code = error.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : null;
                var status = error.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
                var message = error.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;

                var parts = new[] { code, status, message }
                    .Where(part => !string.IsNullOrWhiteSpace(part));

                var detail = string.Join(" | ", parts!);
                return string.IsNullOrWhiteSpace(detail) ? null : detail;
            }
            catch
            {
                return null;
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
            if (TryParseMultiPageGeminiResponse(rawResponse, out var pagePayloads, out _) && pagePayloads.Count > 0)
                return WrapPayloadAsGeminiResponse(pagePayloads[0]);

            try
            {
                using var responseDocument = JsonDocument.Parse(rawResponse);
                if (!responseDocument.RootElement.TryGetProperty("candidates", out var candidates) ||
                    candidates.ValueKind != JsonValueKind.Array ||
                    candidates.GetArrayLength() == 0)
                    return rawResponse;

                var parts = candidates[0].GetProperty("content").GetProperty("parts");
                var textPart = parts.EnumerateArray()
                    .FirstOrDefault(part => part.TryGetProperty("text", out _));

                if (textPart.ValueKind != JsonValueKind.Object ||
                    !textPart.TryGetProperty("text", out var textElement))
                    return rawResponse;

                var candidateText = StripJsonCodeFences(textElement.GetString() ?? string.Empty);
                using var payloadDocument = JsonDocument.Parse(candidateText);

                if (payloadDocument.RootElement.ValueKind == JsonValueKind.Array &&
                    payloadDocument.RootElement.GetArrayLength() > 0)
                    return WrapPayloadAsGeminiResponse(payloadDocument.RootElement[0].Clone());

                if (payloadDocument.RootElement.ValueKind == JsonValueKind.Object)
                    return WrapPayloadAsGeminiResponse(payloadDocument.RootElement.Clone());

                return rawResponse;
            }
            catch
            {
                return rawResponse;
            }
        }

        private static string WrapPayloadAsGeminiResponse(JsonElement payload)
        {
            var structured = SanitizePayloadJson(payload);

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

        private static string SanitizePayloadJson(JsonElement payload)
        {
            if (payload.ValueKind != JsonValueKind.Object)
                return payload.GetRawText();

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in payload.EnumerateObject())
                {
                    if (property.NameEquals("extracted_text") && property.Value.ValueKind == JsonValueKind.String)
                    {
                        writer.WriteString(property.Name, CleanExtractedText(property.Value.GetString() ?? string.Empty));
                        continue;
                    }

                    property.WriteTo(writer);
                }
                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string CleanExtractedText(string value)
        {
            var cleaned = StripJsonCodeFences(value)
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\r", "\r", StringComparison.Ordinal);

            cleaned = TryUnwrapExtractedTextJson(cleaned);
            cleaned = WebUtility.HtmlDecode(cleaned);
            cleaned = Regex.Replace(cleaned, @"</?(html|head|body)\b[^>]*>", string.Empty, RegexOptions.IgnoreCase);
            return cleaned.Trim();
        }

        private static string TryUnwrapExtractedTextJson(string value)
        {
            try
            {
                var candidate = StripJsonCodeFences(value).Trim();
                using var json = JsonDocument.Parse(candidate);
                return ExtractTextFromJsonElement(json.RootElement)?.Trim() ?? value;
            }
            catch
            {
                return value;
            }
        }

        private static string? ExtractTextFromJsonElement(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    var extracted = ExtractTextFromJsonElement(item);
                    if (!string.IsNullOrWhiteSpace(extracted))
                        return extracted;
                }

                return null;
            }

            if (element.ValueKind != JsonValueKind.Object)
                return null;

            if (element.TryGetProperty("extracted_text", out var extractedText) &&
                extractedText.ValueKind == JsonValueKind.String)
            {
                return extractedText.GetString();
            }

            if (element.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String)
            {
                return text.GetString();
            }

            return null;
        }

        private static string? TryExtractGeminiExtractedText(string? rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
                return null;

            try
            {
                using var responseDoc = JsonDocument.Parse(rawResponse);
                if (!responseDoc.RootElement.TryGetProperty("candidates", out var candidates) ||
                    candidates.ValueKind != JsonValueKind.Array ||
                    candidates.GetArrayLength() == 0)
                    return null;

                var parts = candidates[0].GetProperty("content").GetProperty("parts");
                var text = parts.EnumerateArray()
                    .FirstOrDefault(part => part.TryGetProperty("text", out _))
                    .GetProperty("text")
                    .GetString();

                if (string.IsNullOrWhiteSpace(text))
                    return null;

                using var payloadDoc = JsonDocument.Parse(StripJsonCodeFences(text));
                if (payloadDoc.RootElement.ValueKind == JsonValueKind.Array &&
                    payloadDoc.RootElement.GetArrayLength() > 0)
                {
                    var first = payloadDoc.RootElement[0];
                    if (first.TryGetProperty("extracted_text", out var extracted) &&
                        extracted.ValueKind == JsonValueKind.String)
                        return extracted.GetString();
                }

                if (payloadDoc.RootElement.ValueKind == JsonValueKind.Object &&
                    payloadDoc.RootElement.TryGetProperty("extracted_text", out var objectExtracted) &&
                    objectExtracted.ValueKind == JsonValueKind.String)
                    return objectExtracted.GetString();
            }
            catch
            {
                return null;
            }

            return null;
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
