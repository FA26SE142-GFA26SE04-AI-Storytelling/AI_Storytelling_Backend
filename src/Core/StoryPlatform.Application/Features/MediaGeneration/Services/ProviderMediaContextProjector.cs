using System.Text;
using System.Text.Json;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed class ProviderMediaContextProjector : IProviderMediaContextProjector
{
    private const int SupportedSchemaVersion = 2;

    public ProviderMediaContext Project(string contextJson)
    {
        if (string.IsNullOrWhiteSpace(contextJson))
            throw new MediaProviderContextException(MediaProviderContextInvalidReason.EmptyContextJson);

        EnsureNoDuplicateProperties(contextJson);

        using var document = ParseDocument(contextJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new MediaProviderContextException(MediaProviderContextInvalidReason.MalformedJson);

        if (!root.TryGetProperty("schemaVersion", out var versionElement) ||
            versionElement.ValueKind != JsonValueKind.Number ||
            !versionElement.TryGetInt32(out var schemaVersion) ||
            schemaVersion != SupportedSchemaVersion)
        {
            throw new MediaProviderContextException(MediaProviderContextInvalidReason.UnsupportedSchemaVersion);
        }

        var storyFacts = ProjectStoryFacts(root);
        var visualDesign = ProjectVisualDesign(root);
        var projected = new ProviderMediaContext(SupportedSchemaVersion, storyFacts, visualDesign);
        ProviderMediaContextSerializer.Validate(projected);
        return projected;
    }

    private static void EnsureNoDuplicateProperties(string json)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            var stack = new Stack<HashSet<string>>();
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        stack.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;
                    case JsonTokenType.EndObject:
                        if (stack.Count > 0) stack.Pop();
                        break;
                    case JsonTokenType.PropertyName:
                        var name = reader.GetString() ?? string.Empty;
                        if (stack.Count > 0 && !stack.Peek().Add(name))
                            throw new MediaProviderContextException(MediaProviderContextInvalidReason.DuplicateProperty);
                        break;
                }
            }
        }
        catch (JsonException)
        {
            throw new MediaProviderContextException(MediaProviderContextInvalidReason.MalformedJson);
        }
    }

    private static JsonDocument ParseDocument(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new MediaProviderContextException(MediaProviderContextInvalidReason.MalformedJson);
        }
    }

    private static ProviderStoryFacts ProjectStoryFacts(JsonElement root)
    {
        if (!root.TryGetProperty("storyFacts", out var factsElement) ||
            factsElement.ValueKind != JsonValueKind.Object)
        {
            throw new MediaProviderContextException(MediaProviderContextInvalidReason.MissingStoryFacts);
        }

        var title = ReadRequiredString(factsElement, "title", MediaProviderContextInvalidReason.MissingTitle);
        var canonicalContent = ReadRequiredString(
            factsElement, "canonicalContent", MediaProviderContextInvalidReason.MissingCanonicalContent);
        var lesson = ReadOptionalString(factsElement, "lesson");
        var outline = ReadOptionalOutline(factsElement);

        return new ProviderStoryFacts(title, lesson, outline, canonicalContent);
    }

    private static ProviderStoryOutline? ReadOptionalOutline(JsonElement factsElement)
    {
        if (!factsElement.TryGetProperty("approvedOutline", out var outlineElement) ||
            outlineElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (outlineElement.ValueKind != JsonValueKind.Object)
            throw new MediaProviderContextException(MediaProviderContextInvalidReason.InvalidFieldType);

        return new ProviderStoryOutline(
            ReadOptionalString(outlineElement, "outlineOpening"),
            ReadOptionalString(outlineElement, "outlineDevelopment"),
            ReadOptionalString(outlineElement, "outlineEnding"));
    }

    private static ProviderVisualDesign ProjectVisualDesign(JsonElement root)
    {
        if (!root.TryGetProperty("visualDesign", out var visualElement) ||
            visualElement.ValueKind == JsonValueKind.Null)
        {
            return ProviderVisualDesign.CreateDefault();
        }

        if (visualElement.ValueKind != JsonValueKind.Object)
            throw new MediaProviderContextException(MediaProviderContextInvalidReason.InvalidVisualDesign);

        var style = ReadOptionalString(visualElement, "style");
        if (string.IsNullOrWhiteSpace(style))
            style = ProviderVisualDesign.DefaultStyle;

        var continuityRule = ReadOptionalString(visualElement, "continuityRule");
        if (string.IsNullOrWhiteSpace(continuityRule))
            continuityRule = ProviderVisualDesign.DefaultContinuityRule;

        var prohibitedChanges = ReadOptionalStringArray(visualElement, "prohibitedChanges")
            ?? ProviderVisualDesign.DefaultProhibitedChanges;

        return new ProviderVisualDesign(style, continuityRule, prohibitedChanges);
    }

    private static string ReadRequiredString(
        JsonElement parent, string propertyName, MediaProviderContextInvalidReason missingReason)
    {
        if (!parent.TryGetProperty(propertyName, out var element) ||
            element.ValueKind == JsonValueKind.Null)
        {
            throw new MediaProviderContextException(missingReason);
        }

        if (element.ValueKind != JsonValueKind.String)
            throw new MediaProviderContextException(MediaProviderContextInvalidReason.InvalidFieldType);

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new MediaProviderContextException(missingReason);

        return value;
    }

    private static string? ReadOptionalString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var element) ||
            element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
            throw new MediaProviderContextException(MediaProviderContextInvalidReason.InvalidFieldType);

        return element.GetString();
    }

    private static IReadOnlyList<string>? ReadOptionalStringArray(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var element) ||
            element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Array)
            throw new MediaProviderContextException(MediaProviderContextInvalidReason.InvalidFieldType);

        var items = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new MediaProviderContextException(MediaProviderContextInvalidReason.InvalidFieldType);

            var value = item.GetString();
            if (string.IsNullOrWhiteSpace(value))
                throw new MediaProviderContextException(MediaProviderContextInvalidReason.InvalidFieldType);

            items.Add(value);
        }

        return items.Count == 0 ? null : items;
    }
}
