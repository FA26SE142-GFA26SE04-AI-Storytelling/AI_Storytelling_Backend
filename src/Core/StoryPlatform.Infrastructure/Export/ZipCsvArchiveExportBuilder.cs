using System.IO.Compression;
using System.Text;
using StoryPlatform.Application.Abstractions.Export;

namespace StoryPlatform.Infrastructure.Export;

public class ZipCsvArchiveExportBuilder : IArchiveExportBuilder
{
    public byte[] BuildCsvArchive(IReadOnlyList<ExportSheet> sheets)
    {
        if (sheets.Count == 0)
        {
            throw new ArgumentException("Cần ít nhất một sheet để tạo archive.", nameof(sheets));
        }

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var sheet in sheets)
            {
                var entry = archive.CreateEntry($"{sheet.Name}.csv", CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(ToCsvLine(sheet.Columns));
                foreach (var row in sheet.Rows)
                {
                    writer.Write(ToCsvLine(row));
                }
            }
        }

        return stream.ToArray();
    }

    private static string ToCsvLine(IReadOnlyList<string?> fields) =>
        string.Join(',', fields.Select(EscapeCsvField)) + "\r\n";

    private static string EscapeCsvField(string? value)
    {
        value ??= string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
