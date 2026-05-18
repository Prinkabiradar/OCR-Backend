using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Modals;
using OCR_BACKEND.Services;
using System.Data;
using System.Security.Claims;

namespace OCR_BACKEND.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DocumentPdfController : ControllerBase
    {
        private readonly IDocumentPageService _service;

        public DocumentPdfController(IDocumentPageService service)
        {
            _service = service;
        }

        // ── Shared helper ─────────────────────────────────────────────────────
        private static string SanitizeFileName(string name, string fallback)
        {
            string safe = new string(name
                .Where(c => !Path.GetInvalidFileNameChars().Contains(c))
                .ToArray())
                .Trim();

            return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
        }

        [HttpGet("GeneratePdf")]
        public async Task<IActionResult> GeneratePdfByDocumentId([FromQuery] OcrDocumentRequest request)
        {
            try
            {
                //var userClaims = HttpContext.User;
                //var idClaim = userClaims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                //var roleClaim = userClaims.FindFirst(ClaimTypes.Role)?.Value;

                //if (!int.TryParse(idClaim, out _))
                //    return BadRequest("Invalid user ID.");

                //if (!int.TryParse(roleClaim, out int roleId))
                //    return BadRequest("Invalid role ID in token.");

                //request.RoleId = roleId;

                DataTable response = await _service.GetDocumentPagesByDocument(request);

                if (response == null || response.Rows.Count == 0)
                    return NotFound($"No pages found for DocumentId {request.DocumentId}.");

                string documentName = response.Rows[0]["DocumentName"]?.ToString()?.Trim()
                                      ?? string.Empty;

                if (string.IsNullOrWhiteSpace(documentName))
                    documentName = $"Document_{request.DocumentId}";

                byte[] pdfBytes = DocumentPdfGenerator.Generate(
                                      response, request.DocumentId, documentName);

                string baseFileName = documentName?.Trim() ?? "";

                // Remove extension if already present
                baseFileName = Path.GetFileNameWithoutExtension(baseFileName);

                // EXTRA SAFETY: remove trailing ".docx" again if weird cases
                if (baseFileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    baseFileName = baseFileName.Substring(0, baseFileName.Length - 5);
                }

                // If empty → fallback
                if (string.IsNullOrWhiteSpace(baseFileName))
                {
                    baseFileName = $"Document_{request.DocumentId}";
                }

                // Sanitize
                string safeFileName = SanitizeFileName(baseFileName, $"Document_{request.DocumentId}");

                // Final filename must match content type.
                string finalFileName = safeFileName + ".pdf";

                return File(pdfBytes, "application/pdf", finalFileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("GenerateAllPdfs")]
        public async Task<IActionResult> GenerateAllPdfs([FromQuery] OcrDocumentRequest request)
        {
            try
            {
                var userClaims = HttpContext.User;
                var idClaim = userClaims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var roleClaim = userClaims.FindFirst(ClaimTypes.Role)?.Value;

                if (!int.TryParse(idClaim, out int userId))
                    return BadRequest("Invalid user ID.");
                if (!int.TryParse(roleClaim, out int roleId))
                    return BadRequest("Invalid role ID in token.");

                request.RoleId = roleId;

                DataTable response = await _service.GetDocumentPagesByDocument(request);

                if (response == null || response.Rows.Count == 0)
                    return NotFound("No document pages found.");

                // Group rows by DocumentId
                var groupedByDocument = response.AsEnumerable()
                    .GroupBy(row => row.Field<int>("DocumentId"));

                using var zipStream = new MemoryStream();
                using (var archive = new System.IO.Compression.ZipArchive(
                           zipStream,
                           System.IO.Compression.ZipArchiveMode.Create,
                           leaveOpen: true))
                {
                    foreach (var group in groupedByDocument)
                    {
                        int documentId = group.Key;

                        DataTable docTable = response.Clone();
                        foreach (var row in group)
                            docTable.ImportRow(row);

                        string documentName = docTable.Rows[0]["DocumentName"]?.ToString()?.Trim()
                                              ?? string.Empty;

                        if (string.IsNullOrWhiteSpace(documentName))
                            documentName = $"Document_{documentId}";

                        byte[] pdfBytes = DocumentPdfGenerator.Generate(docTable, documentId, documentName);

                        // Use documentName instead of documentId for the zip entry filename
                        string safeFileName = SanitizeFileName(documentName, $"Document_{documentId}");

                        var entry = archive.CreateEntry(
                            $"{safeFileName}.pdf",
                            System.IO.Compression.CompressionLevel.Fastest);

                        using var entryStream = entry.Open();
                        entryStream.Write(pdfBytes, 0, pdfBytes.Length);
                    }
                }

                zipStream.Position = 0;
                return File(zipStream.ToArray(), "application/zip", "AllDocuments.zip");
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("GenerateWord")]
        public async Task<IActionResult> GenerateWordByDocumentId([FromQuery] OcrDocumentRequest request)
        {
            try
            {
                //var userClaims = HttpContext.User;
                //var idClaim = userClaims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                //var roleClaim = userClaims.FindFirst(ClaimTypes.Role)?.Value;

                //if (!int.TryParse(idClaim, out _))
                //    return BadRequest("Invalid user ID.");

                //if (!int.TryParse(roleClaim, out int roleId))
                //    return BadRequest("Invalid role ID in token.");

                //request.RoleId = roleId;

                DataTable response = await _service.GetDocumentPagesByDocument(request);

                if (response == null || response.Rows.Count == 0)
                    return NotFound($"No pages found for DocumentId {request.DocumentId}.");

                // ── Safely read DocumentName handling DBNull explicitly ───────────
                string documentName = response.Rows[0].IsNull("DocumentName")
                    ? string.Empty
                    : response.Rows[0]["DocumentName"].ToString()!.Trim();

                if (string.IsNullOrWhiteSpace(documentName))
                    documentName = $"Document_{request.DocumentId}";

                // ── DEBUG: temporarily uncomment to verify DB value ───────────────
                // return BadRequest(new { documentName, length = documentName.Length });

                byte[] wordBytes = DocumentWordGenerator.Generate(
                                       response, request.DocumentId, documentName);

                // ── Sanitize for filename use ──────────────────────────────────────
                string safeFileName = SanitizeFileName(documentName, $"Document_{request.DocumentId}");

                // ── RFC 5987 encoding for spaces and unicode ───────────────────────
                string encodedFileName = Uri.EscapeDataString(safeFileName + ".docx");

                Response.Headers["Content-Disposition"] =
                    $"attachment; filename=\"{safeFileName}.docx\"; filename*=UTF-8''{encodedFileName}";

                return File(
                    wordBytes,
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                );
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
