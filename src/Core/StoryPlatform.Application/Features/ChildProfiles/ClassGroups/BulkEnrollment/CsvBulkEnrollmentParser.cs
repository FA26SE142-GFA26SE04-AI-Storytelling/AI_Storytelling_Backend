using System.Text;
using StoryPlatform.Application.Common.Exceptions;

namespace StoryPlatform.Application.Features.ChildProfiles.ClassGroups.BulkEnrollment;

public class BulkEnrollmentCsvRow
{
    public int RowNumber { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string AgeBandRaw { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string InviteeEmail { get; set; } = string.Empty;
}

/// <summary>
/// Parses the fixed Nickname,AgeBand,Language,InviteeEmail CSV format used by bulk enrollment.
/// Enforces upload limits, strict UTF-8 decoding, and spreadsheet formula neutralization.
/// </summary>
public static class CsvBulkEnrollmentParser
{
    public const int MaxFileSizeBytes = 2 * 1024 * 1024;
    public const int MaxDataRows = 200;

    private static readonly char[] FormulaTriggerChars = ['=', '+', '-', '@'];

    public static List<BulkEnrollmentCsvRow> Parse(byte[] fileBytes)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);

        if (fileBytes.Length == 0)
        {
            throw new BadRequestException("File CSV rỗng.");
        }

        if (fileBytes.Length > MaxFileSizeBytes)
        {
            throw new BadRequestException(
                $"File CSV vượt quá giới hạn {MaxFileSizeBytes / (1024 * 1024)}MB.");
        }

        string content;
        try
        {
            content = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(fileBytes);
        }
        catch (DecoderFallbackException)
        {
            throw new BadRequestException("File CSV phải được mã hoá UTF-8 hợp lệ.");
        }

        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            throw new BadRequestException("File CSV không có dữ liệu.");
        }

        var dataLines = lines.Skip(1).ToArray();
        if (dataLines.Length > MaxDataRows)
        {
            throw new BadRequestException(
                $"File CSV vượt quá giới hạn {MaxDataRows} dòng dữ liệu mỗi lần import.");
        }

        var rows = new List<BulkEnrollmentCsvRow>();
        for (var i = 0; i < dataLines.Length; i++)
        {
            var fields = SplitCsvLine(dataLines[i]);
            rows.Add(new BulkEnrollmentCsvRow
            {
                RowNumber = i + 2,
                Nickname = Sanitize(GetField(fields, 0)),
                AgeBandRaw = GetField(fields, 1),
                Language = Sanitize(GetField(fields, 2)),
                InviteeEmail = Sanitize(GetField(fields, 3))
            });
        }

        return rows;
    }

    private static string GetField(IReadOnlyList<string> fields, int index) =>
        index < fields.Count ? fields[index].Trim() : string.Empty;

    private static string Sanitize(string value)
    {
        if (value.Length > 0 && FormulaTriggerChars.Contains(value[0]))
        {
            return "'" + value;
        }

        return value;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(character);
                }
            }
            else if (character == '"')
            {
                inQuotes = true;
            }
            else if (character == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
