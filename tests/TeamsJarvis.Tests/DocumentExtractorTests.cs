using System.IO;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using TeamsJarvis.Core;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace TeamsJarvis.Tests;

public class DocumentExtractorTests
{
    [Theory]
    [InlineData(".txt", true)]
    [InlineData(".csv", true)]
    [InlineData(".docx", true)]
    [InlineData(".xlsx", true)]
    [InlineData(".pdf", false)]
    [InlineData(".exe", false)]
    public void IsSupportedMatchesFormats(string ext, bool expected)
    {
        Assert.Equal(expected, DocumentExtractor.IsSupported(ext));
    }

    [Fact]
    public void PdfThrowsNotSupported()
    {
        using var ms = new MemoryStream([1, 2, 3]);
        Assert.Throws<NotSupportedException>(() => DocumentExtractor.ExtractFromStream(ms, ".pdf"));
    }

    [Fact]
    public void ExtractsTextFromCsvStream()
    {
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("a,b,c\n1,2,3"));
        var text = DocumentExtractor.ExtractFromStream(ms, ".csv");
        Assert.Contains("a,b,c", text);
        Assert.Contains("1,2,3", text);
    }

    [Fact]
    public void ExtractsTextFromDocx()
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(
                new W.Paragraph(new W.Run(new W.Text("Estado del pipeline Fabric"))),
                new W.Paragraph(new W.Run(new W.Text("Segunda línea")))));
            main.Document.Save();
        }
        ms.Position = 0;
        var text = DocumentExtractor.ExtractFromStream(ms, ".docx");
        Assert.Contains("Estado del pipeline Fabric", text);
        Assert.Contains("Segunda línea", text);
    }

    [Fact]
    public void ExtractsCellsFromXlsx()
    {
        using var ms = BuildXlsx();
        var text = DocumentExtractor.ExtractFromStream(ms, ".xlsx");
        Assert.Contains("Hoja: Datos", text);
        Assert.Contains("Pipeline", text);
        Assert.Contains("OK", text);
        Assert.Contains("42", text);
    }

    private static MemoryStream BuildXlsx()
    {
        var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new Workbook();
            var wsPart = wbPart.AddNewPart<WorksheetPart>();

            var row1 = new Row(
                new Cell { DataType = CellValues.String, CellValue = new CellValue("Pipeline") },
                new Cell { DataType = CellValues.String, CellValue = new CellValue("OK") });
            var row2 = new Row(
                new Cell { DataType = CellValues.Number, CellValue = new CellValue("42") });
            wsPart.Worksheet = new Worksheet(new SheetData(row1, row2));

            var sheets = wbPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = wbPart.GetIdOfPart(wsPart), SheetId = 1U, Name = "Datos" });
            wbPart.Workbook.Save();
        }
        ms.Position = 0;
        return ms;
    }
}
