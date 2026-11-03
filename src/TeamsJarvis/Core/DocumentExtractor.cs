using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;

namespace TeamsJarvis.Core;

/// <summary>
/// Extrae texto plano de documentos para que el agente pueda "leer" adjuntos: texto plano,
/// docx y xlsx (con DocumentFormat.OpenXml, ya referenciado). PDF no está soportado (requeriría
/// una librería adicional). Sin dependencias de red: todo local.
/// </summary>
public static class DocumentExtractor
{
    public static readonly string[] TextExtensions =
        [".txt", ".md", ".csv", ".json", ".log", ".xml", ".yaml", ".yml", ".sql", ".py", ".cs", ".tsv", ".ini"];

    public static bool IsSupported(string extension)
    {
        extension = extension.ToLowerInvariant();
        return TextExtensions.Contains(extension) || extension is ".docx" or ".xlsx";
    }

    /// <summary>Extrae el texto de un archivo por su ruta. Lanza si el formato no está soportado.</summary>
    public static string ExtractFromFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (TextExtensions.Contains(ext))
            return File.ReadAllText(path);
        using var stream = File.OpenRead(path);
        return ExtractFromStream(stream, ext);
    }

    /// <summary>Extrae el texto de un stream según la extensión. Los formatos de texto se leen como UTF-8.</summary>
    public static string ExtractFromStream(Stream stream, string extension)
    {
        extension = extension.ToLowerInvariant();
        if (TextExtensions.Contains(extension))
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        return extension switch
        {
            ".docx" => ExtractDocx(stream),
            ".xlsx" => ExtractXlsx(stream),
            ".pdf" => throw new NotSupportedException("PDF no está soportado todavía (requeriría una librería externa)."),
            _ => throw new NotSupportedException($"Formato '{extension}' no soportado (soportados: texto, docx, xlsx)."),
        };
    }

    public static string ExtractDocx(Stream stream)
    {
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return "(documento vacío)";
        var sb = new StringBuilder();
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var line = paragraph.InnerText;
            if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine(line);
        }
        return sb.ToString();
    }

    /// <summary>Extrae el texto de un xlsx como filas separadas por tabulador, hoja a hoja.</summary>
    public static string ExtractXlsx(Stream stream)
    {
        using var doc = SpreadsheetDocument.Open(stream, false);
        var workbookPart = doc.WorkbookPart;
        if (workbookPart?.Workbook?.Sheets is null) return "(libro vacío)";

        var shared = workbookPart.SharedStringTablePart?.SharedStringTable;
        var sb = new StringBuilder();

        foreach (var sheet in workbookPart.Workbook.Sheets.Elements<Sheet>())
        {
            if (sheet.Id?.Value is not { } relId) continue;
            if (workbookPart.GetPartById(relId) is not WorksheetPart wsPart) continue;

            sb.AppendLine($"### Hoja: {sheet.Name}");
            var data = wsPart.Worksheet?.GetFirstChild<SheetData>();
            if (data is null) continue;

            foreach (var row in data.Elements<Row>())
            {
                var cells = row.Elements<Cell>().Select(c => CellText(c, shared));
                var line = string.Join("\t", cells).TrimEnd('\t');
                if (line.Length > 0) sb.AppendLine(line);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string CellText(Cell cell, SharedStringTable? shared)
    {
        var value = cell.CellValue?.InnerText ?? "";
        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(value, out var idx) && shared is not null && idx < shared.Count())
            return shared.ElementAt(idx).InnerText;
        return value;
    }
}
