using ImageMagick;

namespace OCR_BACKEND.Services
{
    /// <summary>
    /// Result of a conversion attempt.
    /// </summary>
    public sealed record ConversionResult(
        bool Success,
        string OutputPath,        // path to converted file (PDF or JPEG)
        string OutputMimeType,    // mime of the output file
        string? Error = null
    );

    public interface IFileConversionService
    {
        /// <summary>
        /// Returns true if the extension needs conversion before OCR.
        /// </summary>
        bool NeedsConversion(string filePath);

        /// <summary>
        /// Converts the file at <paramref name="inputPath"/> to a format
        /// accepted by Gemini and writes the result to <paramref name="outputDir"/>.
        /// Returns a <see cref="ConversionResult"/> describing what was produced.
        /// </summary>
        Task<ConversionResult> ConvertAsync(
            string inputPath,
            string outputDir,
            CancellationToken ct = default);
    }

    public sealed class FileConversionService : IFileConversionService
    {
        // ── Formats we can convert ──────────────────────────────────────────
        private static readonly HashSet<string> _officeExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".doc", ".docx", ".ppt", ".pptx", ".odt", ".odp"
        };

        private static readonly HashSet<string> _tiffExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".tif", ".tiff"
        };

        // ── Gemini-accepted mime types (output targets) ─────────────────────
        private const string PdfMime = "application/pdf";
        private const string JpegMime = "image/jpeg";

        private readonly IConfiguration _config;
        private readonly ILogger<FileConversionService> _logger;

        public FileConversionService(IConfiguration config, ILogger<FileConversionService> logger)
        {
            _config = config;
            _logger = logger;
        }

        // ── Public interface ────────────────────────────────────────────────

        public bool NeedsConversion(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return _officeExtensions.Contains(ext) || _tiffExtensions.Contains(ext);
        }

        public async Task<ConversionResult> ConvertAsync(
            string inputPath,
            string outputDir,
            CancellationToken ct = default)
        {
            var ext = Path.GetExtension(inputPath);

            if (_tiffExtensions.Contains(ext))
                return await ConvertTiffToJpegAsync(inputPath, outputDir, ct);

            if (_officeExtensions.Contains(ext))
                return await ConvertOfficeToPdfAsync(inputPath, outputDir, ct);

            return new ConversionResult(false, inputPath, "application/octet-stream",
                $"Extension '{ext}' does not require conversion.");
        }

        // ── TIFF → JPEG (Magick.NET) ────────────────────────────────────────
        // Multi-page TIFFs are flattened: each page becomes a separate JPEG.
        // We return the FIRST page path here; OcrJobService handles multi-page.

        private async Task<ConversionResult> ConvertTiffToJpegAsync(
            string inputPath,
            string outputDir,
            CancellationToken ct)
        {
            try
            {
                var baseName = Path.GetFileNameWithoutExtension(inputPath);
                var outputPath = Path.Combine(outputDir, baseName + "_p1.jpg");

                await Task.Run(() =>
                {
                    using var images = new MagickImageCollection(inputPath);

                    if (images.Count == 0)
                        throw new InvalidOperationException("TIFF file contains no images.");

                    // Page 1 — always produced
                    images[0].Format = MagickFormat.Jpeg;
                    images[0].Quality = 90;
                    images[0].Write(outputPath);

                    // Extra pages — written as _p2.jpg, _p3.jpg, etc.
                    for (int i = 1; i < images.Count; i++)
                    {
                        var extraPath = Path.Combine(outputDir, $"{baseName}_p{i + 1}.jpg");
                        images[i].Format = MagickFormat.Jpeg;
                        images[i].Quality = 90;
                        images[i].Write(extraPath);
                    }
                }, ct);

                _logger.LogInformation("TIFF converted: {In} → {Out}", inputPath, outputPath);
                return new ConversionResult(true, outputPath, JpegMime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TIFF conversion failed for {File}", inputPath);
                return new ConversionResult(false, inputPath, "application/octet-stream", ex.Message);
            }
        }

        // ── Office → PDF (LibreOffice headless CLI) ─────────────────────────
        // LibreOffice must be installed on the host (see header notes).
        // The CLI call is:
        //   soffice --headless --convert-to pdf --outdir <dir> <file>
        //
        // Timeout: configurable via appsettings  "Conversion:LibreOfficeTimeoutSeconds"
        // LibreOffice path: configurable via      "Conversion:LibreOfficePath"

        private async Task<ConversionResult> ConvertOfficeToPdfAsync(
            string inputPath,
            string outputDir,
            CancellationToken ct)
        {
            var loPath = _config["Conversion:LibreOfficePath"] ?? "soffice";
            var timeoutS = _config.GetValue("Conversion:LibreOfficeTimeoutSeconds", 120);

            var baseName = Path.GetFileNameWithoutExtension(inputPath);
            var outputPath = Path.Combine(outputDir, baseName + ".pdf");

            try
            {
                using var cts = CancellationTokenSource
                    .CreateLinkedTokenSource(ct, new CancellationTokenSource(
                        TimeSpan.FromSeconds(timeoutS)).Token);

                var result = await RunProcessAsync(
                    loPath,
                    $"--headless --convert-to pdf --outdir \"{outputDir}\" \"{inputPath}\"",
                    cts.Token);

                if (!result.Success)
                    return new ConversionResult(false, inputPath, PdfMime,
                        $"LibreOffice exited with code {result.ExitCode}: {result.StdErr}");

                if (!File.Exists(outputPath))
                    return new ConversionResult(false, inputPath, PdfMime,
                        "LibreOffice ran successfully but output PDF was not found.");

                _logger.LogInformation("Office→PDF converted: {In} → {Out}", inputPath, outputPath);
                return new ConversionResult(true, outputPath, PdfMime);
            }
            catch (OperationCanceledException)
            {
                return new ConversionResult(false, inputPath, PdfMime,
                    $"LibreOffice conversion timed out after {timeoutS}s.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Office conversion failed for {File}", inputPath);
                return new ConversionResult(false, inputPath, PdfMime, ex.Message);
            }
        }

        // ── Process runner ──────────────────────────────────────────────────

        private sealed record ProcessRunResult(bool Success, int ExitCode, string StdOut, string StdErr);

        private static async Task<ProcessRunResult> RunProcessAsync(
            string fileName, string arguments, CancellationToken ct)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new System.Diagnostics.Process { StartInfo = psi };
            proc.Start();

            var stdOut = await proc.StandardOutput.ReadToEndAsync(ct);
            var stdErr = await proc.StandardError.ReadToEndAsync(ct);

            await proc.WaitForExitAsync(ct);
            return new ProcessRunResult(proc.ExitCode == 0, proc.ExitCode, stdOut, stdErr);
        }
    }
}