using System.Text.RegularExpressions;

namespace StoryPlatform.Application.Features.Moderation;

public sealed record TextDiffEntry(string Type, string? Before, string? After);

/// <summary>Diff văn bản theo câu bằng LCS, không dùng AI.</summary>
public static class TextDiff
{
    public const string Unchanged = "unchanged";
    public const string Added = "added";
    public const string Removed = "removed";
    public const string Modified = "modified";

    private const long MaxCells = 4_000_000;
    private static readonly Regex SentenceBreak = new(@"(?<=[.!?…])\s+", RegexOptions.Compiled);

    public static List<string> SplitUnits(string? text)
    {
        var units = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return units;
        }

        foreach (var paragraph in text.Split('\n'))
        {
            foreach (var sentence in SentenceBreak.Split(paragraph))
            {
                var trimmed = sentence.Trim();
                if (trimmed.Length > 0)
                {
                    units.Add(trimmed);
                }
            }
        }

        return units;
    }

    public static List<TextDiffEntry> Compare(string? before, string? after)
    {
        var a = SplitUnits(before);
        var b = SplitUnits(after);
        if ((long)a.Count * b.Count > MaxCells)
        {
            var replaced = a.Select(unit => new TextDiffEntry(Removed, unit, null)).ToList();
            replaced.AddRange(b.Select(unit => new TextDiffEntry(Added, null, unit)));
            return replaced;
        }

        var table = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
        {
            for (var j = b.Count - 1; j >= 0; j--)
            {
                table[i, j] = a[i] == b[j]
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        var entries = new List<TextDiffEntry>();
        var removed = new List<string>();
        var added = new List<string>();

        void FlushBlock()
        {
            var paired = Math.Min(removed.Count, added.Count);
            for (var index = 0; index < paired; index++)
            {
                entries.Add(new TextDiffEntry(Modified, removed[index], added[index]));
            }
            for (var index = paired; index < removed.Count; index++)
            {
                entries.Add(new TextDiffEntry(Removed, removed[index], null));
            }
            for (var index = paired; index < added.Count; index++)
            {
                entries.Add(new TextDiffEntry(Added, null, added[index]));
            }

            removed.Clear();
            added.Clear();
        }

        var ai = 0;
        var bi = 0;
        while (ai < a.Count && bi < b.Count)
        {
            if (a[ai] == b[bi])
            {
                FlushBlock();
                entries.Add(new TextDiffEntry(Unchanged, a[ai], b[bi]));
                ai++;
                bi++;
            }
            else if (table[ai + 1, bi] >= table[ai, bi + 1])
            {
                removed.Add(a[ai++]);
            }
            else
            {
                added.Add(b[bi++]);
            }
        }

        while (ai < a.Count)
        {
            removed.Add(a[ai++]);
        }
        while (bi < b.Count)
        {
            added.Add(b[bi++]);
        }

        FlushBlock();
        return entries;
    }
}
