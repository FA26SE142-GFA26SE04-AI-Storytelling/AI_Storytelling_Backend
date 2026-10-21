using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StoryPlatform.Contracts.AI.Moderation;

public sealed record RubricExample(string Text, int Level, string Reason);
public sealed record RubricCriterion(string Code, string Name, string Check, string Context,
    Dictionary<string, string> Levels, IReadOnlyList<RubricExample> Examples);
public sealed record RubricDefinition(string Purpose, IReadOnlyList<RubricCriterion> Criteria);
public sealed record RubricSnapshot(int Id, string Version, string SchemaVersion, string ContentHash, RubricDefinition Definition);

/// <summary>The editable rubric cannot alter the five-code protocol, scoring or evidence requirements.</summary>
public static class RubricRules
{
    public const string SchemaVersion = "gray-zone-rubric-schema-v1";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
    public static void Validate(RubricDefinition? definition)
    {
        if (definition is null || !Text(definition.Purpose, 2000) || definition.Criteria is null
            || definition.Criteria.Count != 5 || definition.Criteria.Any(x => x is null)
            || !definition.Criteria.Select(x => x.Code).Order().SequenceEqual(GrayZoneRules.CriterionCodes.Order()))
            throw new ArgumentException("RUBRIC_DEFINITION_INVALID");
        foreach (var c in definition.Criteria)
            if (!Text(c.Name, 200) || !Text(c.Check, 4000) || !Text(c.Context, 4000)
                || c.Levels is null || c.Levels.Count != 5 || Enumerable.Range(0, 5).Any(i => !c.Levels.TryGetValue(i.ToString(), out var text) || !Text(text, 2000))
                || c.Examples is null || c.Examples.Count > 10 || c.Examples.Any(x => x is null || !Text(x.Text, 4000) || x.Level is < 0 or > 4 || !Text(x.Reason, 2000)))
                throw new ArgumentException("RUBRIC_DEFINITION_INVALID");
    }

    public static string Serialize(RubricDefinition definition)
    {
        Validate(definition);
        // Canonical ordering makes hashing independent of JSON property ordering in a client request.
        return JsonSerializer.Serialize(definition with { Criteria = definition.Criteria.OrderBy(c => c.Code)
            .Select(c => c with { Levels = c.Levels.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value) }).ToArray() }, Json);
    }
    public static string Hash(RubricDefinition definition) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(definition))));
    public static void Validate(RubricSnapshot snapshot)
    {
        if (snapshot.Id <= 0 || !Text(snapshot.Version, 50) || snapshot.SchemaVersion != SchemaVersion
            || snapshot.ContentHash != Hash(snapshot.Definition)) throw new ArgumentException("RUBRIC_SNAPSHOT_INVALID");
    }
}
