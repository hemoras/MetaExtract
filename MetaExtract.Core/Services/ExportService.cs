using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using MetaExtract.Core.Models;

namespace MetaExtract.Core.Services;

/// <summary>
/// Exporte les résultats du scan vers un fichier CSV ou Excel, en
/// respectant les colonnes choisies par l'utilisateur et leur ordre.
/// </summary>
public static class ExportService
{
    public static void ExportToCsv(
        IEnumerable<VideoFileRecord> records,
        IReadOnlyList<MetadataFieldDefinition> columns,
        string outputPath)
    {
        using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        writer.WriteLine(string.Join(';', columns.Select(c => EscapeCsv(c.Label))));

        foreach (var record in records)
        {
            var values = columns.Select(c => EscapeCsv(FieldCatalog.GetDisplayValue(record, c.Key)));
            writer.WriteLine(string.Join(';', values));
        }
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    public static void ExportToExcel(
        IEnumerable<VideoFileRecord> records,
        IReadOnlyList<MetadataFieldDefinition> columns,
        string outputPath)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Métadonnées vidéo");

        for (int c = 0; c < columns.Count; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = columns[c].Label;
            cell.Style.Font.Bold = true;
        }

        int row = 2;
        foreach (var record in records)
        {
            for (int c = 0; c < columns.Count; c++)
            {
                sheet.Cell(row, c + 1).Value = FieldCatalog.GetDisplayValue(record, columns[c].Key);
            }
            row++;
        }

        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(1);
        var range = sheet.Range(1, 1, Math.Max(1, row - 1), Math.Max(1, columns.Count));
        range.SetAutoFilter();

        workbook.SaveAs(outputPath);
    }
}
