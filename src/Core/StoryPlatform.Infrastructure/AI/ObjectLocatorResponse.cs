using System.Text.Json;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;

namespace StoryPlatform.Infrastructure.AI;

public static class ObjectLocatorResponse
{
    public const double MinConfidence = 0.5;

    public static LocatedObject? Parse(string modelJson)
    {
        try
        {
            using var document = JsonDocument.Parse(modelJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("found", out var found) || found.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("confidence", out var confidenceElement) ||
                !confidenceElement.TryGetDouble(out var confidence) ||
                !double.IsFinite(confidence) || confidence < MinConfidence || confidence > 1 ||
                !root.TryGetProperty("box_2d", out var coordinates) ||
                coordinates.ValueKind != JsonValueKind.Array || coordinates.GetArrayLength() != 4)
                return null;

            var values = coordinates.EnumerateArray().Select(x => x.GetDouble()).ToArray();
            if (values.Any(x => !double.IsFinite(x) || x < 0 || x > 1000) ||
                values[0] >= values[2] || values[1] >= values[3]) return null;
            var x = values[1] / 1000;
            var y = values[0] / 1000;
            var w = (values[3] - values[1]) / 1000;
            var h = (values[2] - values[0]) / 1000;
            return w * h > 0.8 ? null : new LocatedObject(x, y, w, h, confidence);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
