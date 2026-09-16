namespace StoryPlatform.Application.Abstractions.Export;

public sealed record ExportSheet(string Name, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows);

/// <summary>
/// Dựng file workbook (.xlsx) từ dữ liệu dạng bảng, không phụ thuộc thư viện Excel cụ thể.
/// </summary>
public interface IWorkbookExportBuilder
{
    byte[] BuildWorkbook(IReadOnlyList<ExportSheet> sheets);
}
