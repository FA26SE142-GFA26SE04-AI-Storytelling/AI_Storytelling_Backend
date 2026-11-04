using System.Text.Json;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public static class CreatorMediaSelection
{
    public static string Mode(StoryGenerationJob? job)
    {
        if (string.IsNullOrWhiteSpace(job?.GenerationMetadataJson)) return "auto";
        try
        {
            var metadata = JsonSerializer.Deserialize<JsonElement>(job.GenerationMetadataJson);
            return metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("mediaMode", out var mode) &&
                mode.ValueKind == JsonValueKind.String && mode.GetString() == "choose" ? "choose" : "auto";
        }
        catch (JsonException) { return "auto"; }
    }
}
