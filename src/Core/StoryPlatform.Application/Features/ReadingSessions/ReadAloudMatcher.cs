using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;

namespace StoryPlatform.Application.Features.ReadingSessions;

public static class ReadAloudWordStatus
{
    public const string Good = "good";
    public const string Retry = "retry";
    public const string Skipped = "skipped";
}

public static class ReadAloudHint
{
    /// <summary>Đúng chữ nhưng sai thanh hoặc sai dấu ("con" / "còn").</summary>
    public const string Tone = "tone";
    /// <summary>Đúng chữ nhưng máy nghe không rõ.</summary>
    public const string Unclear = "unclear";
    /// <summary>Đọc thành chữ khác.</summary>
    public const string Different = "different";
}

/// <summary>
/// So khớp chữ trong đoạn truyện với chữ trẻ đọc bằng căn chỉnh Levenshtein theo chữ. Không gọi AI.
/// Sai dấu tốn 0,5 để căn chỉnh vẫn ghép đúng cặp chữ; chữ trẻ nói thừa bị bỏ qua.
/// </summary>
public static class ReadAloudMatcher
{
    private const double ToneOnlyCost = 0.5;
    private static readonly Regex WordPattern = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled);

    public static IReadOnlyList<ReadAloudWordDto> Match(string segmentText, IReadOnlyList<RecognizedWord> spoken)
    {
        var expected = Words(segmentText).ToList();
        var heard = spoken
            .SelectMany(word => Words(word.Text).Select(text => new RecognizedWord(text, word.Confidence)))
            .ToList();

        var cost = new double[expected.Count + 1, heard.Count + 1];
        for (var i = 0; i <= expected.Count; i++) cost[i, 0] = i;
        for (var j = 0; j <= heard.Count; j++) cost[0, j] = j;
        for (var i = 1; i <= expected.Count; i++)
        {
            for (var j = 1; j <= heard.Count; j++)
            {
                cost[i, j] = Math.Min(
                    Math.Min(cost[i - 1, j] + 1, cost[i, j - 1] + 1),
                    cost[i - 1, j - 1] + Substitution(expected[i - 1], heard[j - 1].Text));
            }
        }

        var result = new ReadAloudWordDto[expected.Count];
        int a = expected.Count, b = heard.Count;
        while (a > 0)
        {
            if (b > 0 && cost[a, b] == cost[a - 1, b - 1] + Substitution(expected[a - 1], heard[b - 1].Text))
            {
                result[a - 1] = Judge(a - 1, expected[a - 1], heard[b - 1]);
                a--;
                b--;
            }
            else if (b > 0 && cost[a, b] == cost[a, b - 1] + 1)
            {
                b--;
            }
            else
            {
                result[a - 1] = new ReadAloudWordDto(a - 1, expected[a - 1], ReadAloudWordStatus.Skipped, null);
                a--;
            }
        }

        return result;
    }

    private static ReadAloudWordDto Judge(int index, string expected, RecognizedWord heard)
    {
        if (Normalize(expected) == Normalize(heard.Text))
        {
            return heard.Confidence is double confidence && confidence < ReadAloudPolicy.LowConfidence
                ? new ReadAloudWordDto(index, expected, ReadAloudWordStatus.Retry, ReadAloudHint.Unclear)
                : new ReadAloudWordDto(index, expected, ReadAloudWordStatus.Good, null);
        }

        return new ReadAloudWordDto(index, expected, ReadAloudWordStatus.Retry,
            StripMarks(expected) == StripMarks(heard.Text) ? ReadAloudHint.Tone : ReadAloudHint.Different);
    }

    private static double Substitution(string expected, string heard) =>
        Normalize(expected) == Normalize(heard) ? 0
        : StripMarks(expected) == StripMarks(heard) ? ToneOnlyCost
        : 1;

    private static IEnumerable<string> Words(string text) =>
        WordPattern.Matches(text.Normalize(NormalizationForm.FormC)).Select(match => match.Value);

    private static string Normalize(string word) => word.Normalize(NormalizationForm.FormC).ToLowerInvariant();

    private static string StripMarks(string word)
    {
        var builder = new StringBuilder();
        foreach (var c in Normalize(word).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c == 'đ' ? 'd' : c);
            }
        }

        return builder.ToString();
    }
}
