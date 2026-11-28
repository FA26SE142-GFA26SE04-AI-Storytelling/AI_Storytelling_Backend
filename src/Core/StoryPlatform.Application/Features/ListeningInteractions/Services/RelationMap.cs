using System.Text.Json;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

public sealed record RelationEdge(int From, int To, IReadOnlyList<int> Accepted, string Evidence, string Explanation);

public sealed record RelationMapDefinition(IReadOnlyList<RelationEdge> Edges)
{
    public bool IsValid(int nodeCount, int cardCount) =>
        nodeCount is >= 3 and <= 4 && cardCount is >= 4 and <= 5 && Edges.Count == 3 &&
        Edges.Select(edge => (edge.From, edge.To)).Distinct().Count() == Edges.Count &&
        Edges.All(edge => edge.From >= 0 && edge.From < nodeCount && edge.To >= 0 &&
            edge.To < nodeCount && edge.From != edge.To && edge.Accepted is { Count: > 0 } &&
            edge.Accepted.Distinct().Count() == edge.Accepted.Count &&
            edge.Accepted.All(card => card >= 0 && card < cardCount) &&
            !string.IsNullOrWhiteSpace(edge.Evidence) && !string.IsNullOrWhiteSpace(edge.Explanation));

    public IReadOnlyList<bool> Grade(IReadOnlyDictionary<int, int> cardByEdge) =>
        Edges.Select((edge, index) => cardByEdge.TryGetValue(index, out var card) &&
            edge.Accepted.Contains(card)).ToArray();
}

public static class RelationMap
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static RelationMapDefinition? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<RelationMapDefinition>(json, Options); }
        catch (JsonException) { return null; }
    }
}
