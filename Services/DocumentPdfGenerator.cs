using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using System.Data;

namespace OCR_BACKEND.Services
{
    public static class DocumentPdfGenerator
    {
        public static byte[] Generate(DataTable pages, int documentId)
        {
            using var ms = new MemoryStream();
            using var writer = new PdfWriter(ms);
            using var pdf = new PdfDocument(writer);
            using var doc = new Document(pdf);

            PdfFont bold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            PdfFont normal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            // ✅ Title
            doc.Add(new Paragraph($"Document ID: {documentId}")
                .SetFont(bold)
                .SetFontSize(16)
                .SetMarginBottom(5));

            doc.Add(new Paragraph($"Generated: {DateTime.Now:dd MMM yyyy HH:mm}")
                .SetFont(normal)
                .SetFontSize(12)
                .SetMarginBottom(15));

            foreach (DataRow row in pages.Rows)
            {
                // ✅ Page Header
                doc.Add(new Paragraph($"Page Number: {row["PageNumber"]}")
                    .SetFont(bold)
                    .SetFontSize(13)
                    .SetMarginBottom(5));

                // ✅ Table with proper width
                var table = new Table(new float[] { 150, 350 });
                table.SetWidth(UnitValue.CreatePercentValue(100));


                doc.Add(table);

                // ✅ Extracted Text
                doc.Add(new Paragraph("Extracted Text:")
                    .SetFont(bold)
                    .SetFontSize(10)
                    .SetMarginTop(10));

                doc.Add(new Paragraph(row["ExtractedText"]?.ToString() ?? "(none)")
                    .SetFont(normal)
                    .SetFontSize(9)
                    .SetMarginBottom(20));

                // ✅ Page Break (IMPORTANT)
                doc.Add(new AreaBreak());
            }

            doc.Close();
            return ms.ToArray();
        }

        private static void AddRow(Table table, string key, object value, PdfFont bold, PdfFont normal)
        {
            table.AddCell(new Cell()
                .Add(new Paragraph(key).SetFont(bold).SetFontSize(9)));

            table.AddCell(new Cell()
                .Add(new Paragraph(value?.ToString() ?? "-").SetFont(normal).SetFontSize(9)));
        }
    }
}