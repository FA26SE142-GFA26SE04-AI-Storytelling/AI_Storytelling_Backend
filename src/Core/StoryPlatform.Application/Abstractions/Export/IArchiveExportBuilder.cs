namespace StoryPlatform.Application.Abstractions.Export;

/// <summary>
/// Dựng file .zip chứa một .csv cho mỗi ExportSheet — dùng khi Export cần định dạng CSV
/// thay vì .xlsx (CSV không hỗ trợ multi-sheet trong 1 file).
/// </summary>
public interface IArchiveExportBuilder
{
    byte[] BuildCsvArchive(IReadOnlyList<ExportSheet> sheets);
}
