using ClosedXML.Excel;
using StoryPlatform.Application.Abstractions.Export;

namespace StoryPlatform.Infrastructure.Export;

public class ClosedXmlWorkbookExportBuilder : IWorkbookExportBuilder
{
    public byte[] BuildWorkbook(IReadOnlyList<ExportSheet> sheets)
    {
        if (sheets.Count == 0)
        {
            throw new ArgumentException("Cần ít nhất một sheet để tạo workbook.", nameof(sheets));
        }

        using var workbook = new XLWorkbook();
        foreach (var sheet in sheets)
        {
            var worksheet = workbook.Worksheets.Add(sheet.Name);
            for (var columnIndex = 0; columnIndex < sheet.Columns.Count; columnIndex++)
            {
                worksheet.Cell(1, columnIndex + 1).Value = sheet.Columns[columnIndex];
            }

            for (var rowIndex = 0; rowIndex < sheet.Rows.Count; rowIndex++)
            {
                var row = sheet.Rows[rowIndex];
                for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
                {
                    worksheet.Cell(rowIndex + 2, columnIndex + 1).Value = row[columnIndex] ?? string.Empty;
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
