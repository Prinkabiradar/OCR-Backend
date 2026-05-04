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
        [HttpGet("GeneratePdf")]
        public async Task<IActionResult> GeneratePdfByDocumentId([FromQuery] OcrDocumentRequest request)
        {
            try
            {
                var userClaims = HttpContext.User;
                var idClaim = userClaims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var RoleIdClaim = userClaims.FindFirst(ClaimTypes.Role)?.Value;
                if (!int.TryParse(idClaim, out int Id))
                    return BadRequest("Invalid user ID.");
                if (!int.TryParse(RoleIdClaim, out int RoleId))
                {
                    return BadRequest("Invalid employee ID in token.");
                }

               // request.UserId = Id;
                request.RoleId = RoleId;
                // ── Auth claims ────────────────────────────────────────────
                //var userClaims = HttpContext.User;
               // var idClaim = userClaims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var roleClaim = userClaims.FindFirst(ClaimTypes.Role)?.Value;

                if (!int.TryParse(idClaim, out int userId))
                    return BadRequest("Invalid user ID.");
                if (!int.TryParse(roleClaim, out int roleId))
                    return BadRequest("Invalid role ID in token.");

                request.RoleId = roleId;

                // ── Fetch pages for the requested DocumentId ───────────────
                DataTable response = await _service.GetDocumentPagesByDocument(request);

                if (response == null || response.Rows.Count == 0)
                    return NotFound($"No pages found for DocumentId {request.DocumentId}.");

                // ── Build PDF in memory ────────────────────────────────────
                string documentName = response.Rows[0]["DocumentName"]?.ToString() ?? $"Document {request.DocumentId}";
                byte[] pdfBytes = DocumentPdfGenerator.Generate(response, request.DocumentId, documentName);

                string fileName = $"Document_{request.DocumentId}.pdf";
                return File(pdfBytes, "application/pdf", fileName);
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
                // ── Auth claims ────────────────────────────────────────────
                var userClaims = HttpContext.User;
                var idClaim = userClaims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var roleClaim = userClaims.FindFirst(ClaimTypes.Role)?.Value;

                if (!int.TryParse(idClaim, out int userId))
                    return BadRequest("Invalid user ID.");
                if (!int.TryParse(roleClaim, out int roleId))
                    return BadRequest("Invalid role ID in token.");

                request.RoleId = roleId;

                // ── Fetch ALL pages ────────────────────────────────────────
                DataTable response = await _service.GetDocumentPagesByDocument(request);

                if (response == null || response.Rows.Count == 0)
                    return NotFound("No document pages found.");

                // ── Group rows by DocumentId ───────────────────────────────
                var groupedByDocument = response.AsEnumerable()
                    .GroupBy(row => row.Field<int>("DocumentId"));

                // ── Build a ZIP containing one PDF per DocumentId ──────────
                using var zipStream = new MemoryStream();
                using (var archive = new System.IO.Compression.ZipArchive(
                           zipStream,
                           System.IO.Compression.ZipArchiveMode.Create,
                           leaveOpen: true))
                {
                    foreach (var group in groupedByDocument)
                    {
                        int documentId = group.Key;

                        // Create a DataTable for just this document's rows
                        DataTable docTable = response.Clone(); // same schema
                        foreach (var row in group)
                            docTable.ImportRow(row);

                        string documentName = docTable.Rows[0]["DocumentName"]?.ToString() ?? $"Document {documentId}";
                        byte[] pdfBytes = DocumentPdfGenerator.Generate(docTable, documentId, documentName);

                        var entry = archive.CreateEntry(
                            $"Document_{documentId}.pdf",
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
                var userClaims = HttpContext.User;
                var idClaim = userClaims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var roleClaim = userClaims.FindFirst(ClaimTypes.Role)?.Value;

                if (!int.TryParse(idClaim, out _))
                    return BadRequest("Invalid user ID.");
                if (!int.TryParse(roleClaim, out int roleId))
                    return BadRequest("Invalid role ID in token.");

                request.RoleId = roleId;

                DataTable response = await _service.GetDocumentPagesByDocument(request);

                if (response == null || response.Rows.Count == 0)
                    return NotFound($"No pages found for DocumentId {request.DocumentId}.");

                string documentName = response.Rows[0]["DocumentName"]?.ToString()
                                      ?? $"Document {request.DocumentId}";

                byte[] wordBytes = DocumentWordGenerator.Generate(
                                       response, request.DocumentId, documentName);

                return File(
                    wordBytes,
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    $"Document_{request.DocumentId}.docx"
                );
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}