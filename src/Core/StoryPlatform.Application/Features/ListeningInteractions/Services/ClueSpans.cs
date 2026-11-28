namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

/// <summary>Suy luận từ chi tiết (tuổi 8): vị trí từng manh mối trong đoạn để tô sáng.</summary>
public static class ClueSpans
{
    /// <returns>Các đoạn [Start, End) theo thứ tự xuất hiện; null nếu có manh mối không nằm trong đoạn, trùng nhau hoặc chồng lấn.</returns>
    public static IReadOnlyList<(int Start, int End)>? Find(string passage, IReadOnlyList<string> clues)
    {
        var spans = new List<(int Start, int End)>();
        foreach (var clue in clues)
        {
            var text = clue.Trim();
            var start = text.Length == 0 ? -1 : passage.IndexOf(text, StringComparison.Ordinal);
            if (start < 0) return null;
            spans.Add((start, start + text.Length));
        }
        var ordered = spans.OrderBy(s => s.Start).ToList();
        for (var i = 1; i < ordered.Count; i++)
            if (ordered[i].Start < ordered[i - 1].End) return null;
        return ordered;
    }
}
