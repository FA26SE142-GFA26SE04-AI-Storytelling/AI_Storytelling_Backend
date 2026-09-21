using System.Text;
using System.Text.RegularExpressions;

namespace StoryPlatform.Application.Features.ExistingStories.Helpers;

/// <summary>
/// Chuẩn hoá nội dung truyện nhập vào từ Parent/Teacher.
/// Loại bỏ BOM, null chars, chuẩn hoá newline, nén khoảng trắng thừa.
/// </summary>
internal static class StoryContentNormalizer
{
    private static readonly Regex MultipleBlankLines = new(@"\r?\n[ \t]*\r?\n([ \t]*\r?\n)+", RegexOptions.Compiled);
    private static readonly Regex TrailingWhitespace = new(@"[ \\t]+(?=\r?\n)", RegexOptions.Compiled);
    private static readonly Regex MultipleSpaces = new(@"[ \t]{2,}", RegexOptions.Compiled);

    /// <summary>
    /// Chuẩn hoá nội dung. Trả về <c>null</c> nếu đầu vào rỗng sau khi chuẩn hoá.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (raw is null) return null;
        var s = raw;

        // Strip BOM
        if (s.Length > 0 && s[0] == '\uFEFF') s = s[1..];

        // Strip null chars
        s = s.Replace("\0", string.Empty);

        // Normalize CRLF/CR -> LF
        s = s.Replace("\r\n", "\n").Replace("\r", "\n");

        // Compress multiple blank lines
        s = MultipleBlankLines.Replace(s, "\n\n");

        // Trim trailing spaces per line
        s = TrailingWhitespace.Replace(s, string.Empty);

        // Compress runs of spaces/tabs (but preserve newlines and single spaces)
        s = MultipleSpaces.Replace(s, " ");

        return s.Trim();
    }

    /// <summary>
    /// Đếm từ đơn giản theo whitespace.
    /// </summary>
    public static int CountWords(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0;
        var words = content.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        return words.Length;
    }
}
