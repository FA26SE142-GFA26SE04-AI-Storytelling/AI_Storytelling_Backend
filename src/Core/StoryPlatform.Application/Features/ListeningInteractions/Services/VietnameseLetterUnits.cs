using System.Globalization;
using System.Text;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

/// <summary>Vietnamese letter tiles and system-authored pronunciation for base letters.</summary>
public static class VietnameseLetterUnits
{
    private static readonly string[] Digraphs = ["ngh", "ng", "nh", "ch", "gh", "gi", "kh", "ph", "qu", "th", "tr"];
    private static readonly Dictionary<string, string> Sounds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["b"] = "bờ", ["c"] = "cờ", ["d"] = "dờ", ["g"] = "gờ", ["h"] = "hờ", ["k"] = "ca", ["l"] = "lờ",
        ["m"] = "mờ", ["n"] = "nờ", ["p"] = "pờ", ["r"] = "rờ", ["s"] = "sờ", ["t"] = "tờ", ["v"] = "vờ", ["x"] = "xờ",
        ["a"] = "a", ["e"] = "e", ["i"] = "i", ["o"] = "o", ["u"] = "u", ["y"] = "i",
        ["ngh"] = "ngờ", ["ng"] = "ngờ", ["nh"] = "nhờ", ["ch"] = "chờ", ["gh"] = "gờ", ["gi"] = "giờ",
        ["kh"] = "khờ", ["ph"] = "phờ", ["qu"] = "quờ", ["th"] = "thờ", ["tr"] = "trờ"
    };

    public static IReadOnlyList<string> BaseLetters { get; } = Array.AsReadOnly(Sounds.Keys.ToArray());

    public static IReadOnlyList<string> Split(string word)
    {
        word = word.Normalize(NormalizationForm.FormC);
        var units = new List<string>();
        for (var i = 0; i < word.Length;)
        {
            var digraph = Digraphs.FirstOrDefault(d => i + d.Length <= word.Length &&
                string.Equals(word.Substring(i, d.Length), d, StringComparison.OrdinalIgnoreCase) &&
                (d != "gi" || (i + 2 < word.Length && IsVowel(word[i + 2]))));
            var length = digraph?.Length ?? 1;
            units.Add(word.Substring(i, length));
            i += length;
        }
        return units;
    }

    public static bool IsBlankable(string unit) => Sounds.ContainsKey(unit) &&
        unit.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');

    public static string? SoundOf(string unit) => Sounds.GetValueOrDefault(unit);

    private static bool IsVowel(char c) => "aeiouy".Contains(char.ToLowerInvariant(
        c.ToString().Normalize(NormalizationForm.FormD)
            .First(x => CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark)));
}
