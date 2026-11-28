using System.Text.Json;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

public static class SentenceOrders
{
    public const int MaxOrders = 6;

    public static IReadOnlyList<IReadOnlyList<int>>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<int[][]>(json)?.Select(order => (IReadOnlyList<int>)order).ToArray(); }
        catch (JsonException) { return null; }
    }

    public static bool IsValid(IReadOnlyList<IReadOnlyList<int>>? orders, int cardCount) =>
        orders is not null && cardCount is >= 3 and <= 5 && orders.Count is >= 1 and <= MaxOrders &&
        orders.All(order => order.Count == cardCount &&
            order.OrderBy(value => value).SequenceEqual(Enumerable.Range(1, cardCount))) &&
        orders.Select(order => string.Join(',', order)).Distinct().Count() == orders.Count;

    public static bool Matches(IReadOnlyList<IReadOnlyList<int>> orders, IReadOnlyList<int> chosen) =>
        orders.Any(order => order.SequenceEqual(chosen));

    public static IReadOnlyList<int> Canonical(IReadOnlyList<IReadOnlyList<int>> orders) => orders[0];
}
