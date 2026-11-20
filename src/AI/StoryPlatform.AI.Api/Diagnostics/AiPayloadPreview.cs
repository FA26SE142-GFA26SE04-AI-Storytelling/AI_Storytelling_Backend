using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StoryPlatform.AI.Api.Diagnostics;

public static partial class AiPayloadPreview
{
    private const int MaxDepth = 12;
    private const int MaxArrayItems = 30;
    private const int MaxVisitedNodes = 256;
    private const int MaxCleanInputCharacters = 65536;
    private const int RegexTimeoutMs = 100;

    private static readonly HashSet<string> AllowlistedMetadataProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "requestId", "traceId", "jobId", "taskId", "storyId", "versionId", "packageRevision",
        "assetId", "sceneIndex", "beatOrder", "attempt", "attemptCount", "status", "statusCode",
        "errorCode", "provider", "model", "operation", "stage",
        "durationMs", "elapsedMs", "mimeType", "aspectRatio", "languageCode", "voiceName",
        "isSafe", "passed", "score", "alignmentScore", "containsVisibleText",
        "characters", "requestCharacters", "bytes", "responseBytes", "count", "total"
    };

    public static string Format(object? value, int maximumCharacters, IReadOnlyList<string>? secrets = null) =>
        FormatCore(value, maximumCharacters, secrets, allowFreeText: true);

    public static string FormatAllowlisted(object? value, int maximumCharacters, IReadOnlyList<string>? secrets = null) =>
        FormatCore(value, maximumCharacters, secrets, allowFreeText: false);

    private static string FormatCore(
        object? value,
        int maximumCharacters,
        IReadOnlyList<string>? secrets,
        bool allowFreeText)
    {
        try
        {
            var safeSecrets = secrets ?? [];
            var node = JsonSerializer.SerializeToNode(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (node is JsonValue scalar && scalar.TryGetValue<string>(out var text))
            {
                node = JsonValue.Create(allowFreeText
                    ? Clean(text, safeSecrets)
                    : $"[OMITTED len={text.Length}]");
            }
            else
            {
                var visited = 0;
                Scrub(node, safeSecrets, 0, allowFreeText, ref visited);
            }

            var json = node?.ToJsonString(new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }) ?? "null";
            json = json.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

            var limit = Math.Clamp(maximumCharacters, 256, 32768);
            return json.Length <= limit ? json : json[..limit] + " [TRUNCATED]";
        }
        catch (Exception)
        {
            return "[PAYLOAD_UNAVAILABLE]";
        }
    }

    private static void Scrub(
        JsonNode? node,
        IReadOnlyList<string> secrets,
        int depth,
        bool allowFreeText,
        ref int visited)
    {
        if (node is null)
            return;

        if (node is JsonObject obj)
        {
            foreach (var entry in obj.ToArray())
            {
                if (SensitiveName().IsMatch(entry.Key))
                {
                    obj[entry.Key] = "[REDACTED]";
                    continue;
                }

                if (++visited > MaxVisitedNodes || depth >= MaxDepth)
                {
                    obj[entry.Key] = "[OMITTED]";
                    continue;
                }

                if (entry.Value is JsonValue scalar)
                {
                    if (scalar.TryGetValue<string>(out var text))
                    {
                        obj[entry.Key] = allowFreeText || AllowlistedMetadataProperties.Contains(entry.Key)
                            ? Clean(text, secrets)
                            : $"[OMITTED len={text.Length}]";
                    }
                }
                else if (entry.Value is not null)
                {
                    if (visited >= MaxVisitedNodes || depth + 1 >= MaxDepth)
                        obj[entry.Key] = "[OMITTED]";
                    else
                        Scrub(entry.Value, secrets, depth + 1, allowFreeText, ref visited);
                }
            }
        }
        else if (node is JsonArray array)
        {
            while (array.Count > MaxArrayItems)
                array.RemoveAt(array.Count - 1);

            for (var i = 0; i < array.Count; i++)
            {
                if (++visited > MaxVisitedNodes || depth >= MaxDepth)
                {
                    array[i] = "[OMITTED]";
                }
                else if (array[i] is JsonValue scalar)
                {
                    if (scalar.TryGetValue<string>(out var text))
                        array[i] = allowFreeText ? Clean(text, secrets) : $"[OMITTED len={text.Length}]";
                }
                else if (array[i] is not null)
                {
                    if (visited >= MaxVisitedNodes || depth + 1 >= MaxDepth)
                        array[i] = "[OMITTED]";
                    else
                        Scrub(array[i], secrets, depth + 1, allowFreeText, ref visited);
                }
            }
        }
    }

    private static string Clean(string text, IReadOnlyList<string> secrets)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (text.Length > MaxCleanInputCharacters)
            return $"[OMITTED len={text.Length}]";

        var cleaned = text;
        foreach (var secret in secrets.Where(x => x.Length >= 4).OrderByDescending(x => x.Length))
            cleaned = cleaned.Replace(secret, "[REDACTED]", StringComparison.Ordinal);

        cleaned = PrivateKey().Replace(cleaned, "[REDACTED]");
        cleaned = UrlWithUserInfo().Replace(cleaned, "[REDACTED_URL]");
        cleaned = UrlWithSecretQuery().Replace(cleaned, "[REDACTED_URL]");
        cleaned = Credential().Replace(cleaned, "[REDACTED]");
        cleaned = Email().Replace(cleaned, "[REDACTED_EMAIL]");
        cleaned = Phone().Replace(cleaned, "[REDACTED_PHONE]");
        return cleaned.Length <= 8192 ? cleaned : cleaned[..8192] + " [TRUNCATED]";
    }

    [GeneratedRegex("password|secret|token|authorization|credential|api[_-]?key|connectionstring", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex SensitiveName();

    [GeneratedRegex(@"-----BEGIN\s+(?:[A-Z0-9 ]+\s+)?PRIVATE KEY-----", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex PrivateKey();

    [GeneratedRegex(@"https?://[^/\s:@]+:[^/\s@]+@[^\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex UrlWithUserInfo();

    [GeneratedRegex(@"https?://[^\s]+[?&](?:token|access_token|id_token|api_key|apikey|key|secret|password|sig|signature|x-amz-signature|x-goog-signature|auth)=[^\s&]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex UrlWithSecretQuery();

    [GeneratedRegex(@"Bearer\s+\S+|eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+|AIza[A-Za-z0-9_-]{20,}|sk-[A-Za-z0-9_-]{16,}|ghp_[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex Credential();

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex Email();

    [GeneratedRegex(@"(?:(?<!\d)(?:\+?84|0)(?:[ .-]?\d){9,10}(?!\d)|(?<!\d)\+\d{1,3}[ .-]?(?:\(\d{1,4}\)[ .-]?)?(?:\d[ .-]?){7,12}\d(?!\d)|\(\d{2,4}\)[ .-]?\d{3,4}[ .-]?\d{3,4}(?!\d))", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex Phone();
}
