using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StoryPlatform.AI.Api.Diagnostics;

public static partial class AiPayloadPreview
{
    public static string Format(object? value, int maximumCharacters, IReadOnlyList<string>? secrets = null)
    {
        try
        {
            var node = JsonSerializer.SerializeToNode(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (node is JsonValue scalar && scalar.TryGetValue<string>(out var text))
                node = JsonValue.Create(Clean(text, secrets ?? []));
            Scrub(node, secrets ?? [], 0);
            var json = node?.ToJsonString(new JsonSerializerOptions
            { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? "null";
            var limit = Math.Clamp(maximumCharacters, 256, 32768);
            return json.Length <= limit ? json : json[..limit] + " [TRUNCATED]";
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            return "[PAYLOAD_UNAVAILABLE]";
        }
    }

    private static void Scrub(JsonNode? node, IReadOnlyList<string> secrets, int depth)
    {
        if (node is JsonObject obj)
        {
            foreach (var entry in obj.ToArray())
            {
                if (depth >= 12 || SensitiveName().IsMatch(entry.Key)) obj[entry.Key] = "[REDACTED]";
                else if (entry.Value is JsonValue scalar && scalar.TryGetValue<string>(out var text)) obj[entry.Key] = Clean(text, secrets);
                else Scrub(entry.Value, secrets, depth + 1);
            }
        }
        else if (node is JsonArray array)
        {
            while (array.Count > 30) array.RemoveAt(array.Count - 1);
            for (var i = 0; i < array.Count; i++)
            {
                if (depth >= 12) array[i] = "[OMITTED]";
                else if (array[i] is JsonValue scalar && scalar.TryGetValue<string>(out var text)) array[i] = Clean(text, secrets);
                else Scrub(array[i], secrets, depth + 1);
            }
        }
    }

    private static string Clean(string text, IReadOnlyList<string> secrets)
    {
        foreach (var secret in secrets.Where(x => x.Length >= 4).OrderByDescending(x => x.Length))
            text = text.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        return Credential().Replace(text, "[REDACTED]");
    }

    [GeneratedRegex("password|secret|token|authorization|credential|api[_-]?key|connectionstring", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveName();

    [GeneratedRegex(@"Bearer\s+\S+|eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+|AIza[A-Za-z0-9_-]{20,}|sk-[A-Za-z0-9_-]{16,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Credential();
}
