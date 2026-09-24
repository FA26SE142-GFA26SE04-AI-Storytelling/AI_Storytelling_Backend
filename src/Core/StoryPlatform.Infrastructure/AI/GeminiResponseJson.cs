using System.Text.Json;

namespace StoryPlatform.Infrastructure.AI;

internal static class GeminiResponseJson
{
    public static string ExtractFirstTextPayload(string response)
    {
        using var envelope = JsonDocument.Parse(response);
        var root = envelope.RootElement;
        if (!root.TryGetProperty("candidates", out var candidates))
            return StripCodeFence(response);
        if (candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            throw new JsonException("Gemini response has no candidates.");

        var parts = candidates[0].GetProperty("content").GetProperty("parts");
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                return StripCodeFence(text.GetString() ?? string.Empty);
        }

        throw new JsonException("Gemini response has no text part.");
    }

    private static string StripCodeFence(string value)
    {
        var text = value.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;

        var firstLine = text.IndexOf('\n');
        if (firstLine < 0)
            return text;
        text = text[(firstLine + 1)..];
        var closing = text.LastIndexOf("```", StringComparison.Ordinal);
        return (closing >= 0 ? text[..closing] : text).Trim();
    }
}
