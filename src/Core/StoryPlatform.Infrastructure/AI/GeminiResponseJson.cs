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

        var fenceStart = text.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var firstLineAfterFence = text.IndexOf('\n', fenceStart);
            if (firstLineAfterFence >= 0)
            {
                var contentStart = firstLineAfterFence + 1;
                var fenceEnd = text.IndexOf("```", contentStart, StringComparison.Ordinal);
                if (fenceEnd >= 0)
                {
                    return text[contentStart..fenceEnd].Trim();
                }
            }
        }

        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return text[firstBrace..(lastBrace + 1)].Trim();
        }

        var firstBracket = text.IndexOf('[');
        var lastBracket = text.LastIndexOf(']');
        if (firstBracket >= 0 && lastBracket > firstBracket)
        {
            return text[firstBracket..(lastBracket + 1)].Trim();
        }

        return text;
    }
}
