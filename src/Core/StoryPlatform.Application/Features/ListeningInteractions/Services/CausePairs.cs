using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

/// <summary>Nối nguyên nhân và kết quả (tuổi 8): 3 thẻ kết quả (Item) và 3 thẻ lí do (Target), hai thẻ cùng số cặp là một cặp.</summary>
public static class CausePairs
{
    public const int PairCount = 3;

    public static bool IsValid(IReadOnlyList<(ListeningOptionRole Role, int? Pair)> cards)
    {
        var items = cards.Where(c => c.Role == ListeningOptionRole.Item).Select(c => c.Pair).ToArray();
        var targets = cards.Where(c => c.Role == ListeningOptionRole.Target).Select(c => c.Pair).ToArray();
        if (cards.Count != PairCount * 2 || items.Length != PairCount || targets.Length != PairCount) return false;
        var expected = Enumerable.Range(1, PairCount).Select(n => (int?)n).ToArray();
        return items.OrderBy(x => x).SequenceEqual(expected) && targets.OrderBy(x => x).SequenceEqual(expected);
    }

    public static bool Matches(ListeningInteractionOption effect, ListeningInteractionOption cause) =>
        effect.Role == ListeningOptionRole.Item && cause.Role == ListeningOptionRole.Target &&
        effect.CorrectOrder is not null && effect.CorrectOrder == cause.CorrectOrder;

    /// <summary>"Vì {lí do} nên {kết quả}." — lí do và kết quả viết thường chữ đầu, bỏ dấu câu cuối.</summary>
    public static string Sentence(string cause, string effect) =>
        $"Vì {Lower(Trim(cause))} nên {Lower(Trim(effect))}.";

    private static string Trim(string s) => s.Trim().TrimEnd('.', '!', '?', ',', ';');
    private static string Lower(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
