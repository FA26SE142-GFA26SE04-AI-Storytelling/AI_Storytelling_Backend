using System.Text;
using System.Text.Json;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

/// <summary>
/// Căn chữ nghe được trong audio (từ STT) vào các token chữ của văn bản gốc để lấy mốc từng chữ.
/// Văn bản truyện không bao giờ bị đổi; transcript chỉ là bằng chứng. Chữ không nghe khớp chỉ được nội suy
/// giữa hai chữ đã khớp, và chỉ khi độ phủ đủ cao, nên không bao giờ có mốc "bịa" từ độ dài tổng.
/// </summary>
public static class WordTimingAligner
{
    /// <returns>JSON <c>[{word,start,end}]</c> (một phần tử cho mỗi token chữ), hoặc null nếu không đủ độ phủ.</returns>
    public static string? BuildWordTimingsJson(
        IReadOnlyList<TimingMark> tokens, IReadOnlyList<TimedWord> heard, double minCoverage)
    {
        var source = tokens.Where(t => t.IsWord).ToList();
        if (source.Count == 0 || heard.Count == 0) return null;

        var sourceKeys = source.Select(t => Normalize(t.Text)).ToArray();
        var heardKeys = heard.Select(h => Normalize(h.Text)).ToArray();
        var match = LongestCommonSubsequence(sourceKeys, heardKeys);   // match[i] = chỉ số heard khớp với source i, hoặc -1

        var matched = match.Count(m => m >= 0);
        if (matched == 0 || (double)matched / source.Count < minCoverage) return null;

        var start = new double[source.Count];
        var end = new double[source.Count];
        for (var i = 0; i < source.Count; i++)
        {
            if (match[i] < 0) continue;
            start[i] = heard[match[i]].StartSeconds;
            end[i] = Math.Max(heard[match[i]].EndSeconds, heard[match[i]].StartSeconds);
        }

        FillGaps(source, match, heard, start, end);

        var entries = new List<object>(source.Count);
        var previousEnd = 0.0;
        for (var i = 0; i < source.Count; i++)
        {
            var s = Math.Max(start[i], previousEnd);
            var e = Math.Max(end[i], s);
            previousEnd = e;
            entries.Add(new { word = source[i].Text, start = Math.Round(s, 3), end = Math.Round(e, 3) });
        }

        return JsonSerializer.Serialize(entries, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    /// <summary>Nội suy các chữ chưa khớp theo độ dài chữ, trong khoảng giữa hai chữ đã khớp (hoặc đầu/cuối audio nghe được).</summary>
    private static void FillGaps(List<TimingMark> source, int[] match, IReadOnlyList<TimedWord> heard, double[] start, double[] end)
    {
        var i = 0;
        while (i < source.Count)
        {
            if (match[i] >= 0) { i++; continue; }
            var from = i;
            while (i < source.Count && match[i] < 0) i++;
            var to = i;   // [from, to) là đoạn chưa khớp

            var gapStart = from > 0 ? end[from - 1] : heard[0].StartSeconds;
            var gapEnd = to < source.Count ? start[to] : heard[^1].EndSeconds;
            if (gapEnd < gapStart) gapEnd = gapStart;

            var weights = Enumerable.Range(from, to - from).Select(k => Math.Max(1, source[k].Text.Length)).ToArray();
            var total = weights.Sum();
            var cursor = gapStart;
            for (var k = from; k < to; k++)
            {
                var span = (gapEnd - gapStart) * weights[k - from] / total;
                start[k] = cursor;
                end[k] = cursor + span;
                cursor += span;
            }
        }
    }

    /// <summary>Chuẩn hóa chỉ để so sánh: NFC, chữ thường, bỏ dấu câu/khoảng trắng; giữ dấu thanh tiếng Việt.</summary>
    internal static string Normalize(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.Normalize(NormalizationForm.FormC).ToLowerInvariant())
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    private static int[] LongestCommonSubsequence(string[] a, string[] b)
    {
        var n = a.Length; var m = b.Length;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                dp[i, j] = a[i].Length > 0 && a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        var result = Enumerable.Repeat(-1, n).ToArray();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x].Length > 0 && a[x] == b[y]) { result[x] = y; x++; y++; }
            else if (dp[x + 1, y] >= dp[x, y + 1]) x++;
            else y++;
        }
        return result;
    }
}
