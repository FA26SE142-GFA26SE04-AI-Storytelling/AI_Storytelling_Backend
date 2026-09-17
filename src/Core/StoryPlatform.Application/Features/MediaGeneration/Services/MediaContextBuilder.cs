using System.Text.Json;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed class MediaContextBuilder : IMediaContextBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Build(MediaContextBuildRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content)) throw new InvalidOperationException("EMPTY_CANONICAL_CONTENT");
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            sourceStoryVersionId = request.StoryVersionId,
            storyFacts = new
            {
                request.Title,
                request.Lesson,
                approvedOutline = new { request.OutlineOpening, request.OutlineDevelopment, request.OutlineEnding },
                canonicalContent = request.Content,
                acceptedInput = ParseJson(request.AcceptedInputJson),
                acceptedContext = ParseJson(request.ContextSnapshotJson)
            },
            visualDesign = new
            {
                style = "child-friendly storybook illustration",
                continuityRule = "Keep character identity, clothing, important objects, locations and timeline consistent.",
                prohibitedChanges = new[] { "plot", "lesson", "character identity", "canonical scene text" }
            }
        }, JsonOptions);
    }

    private static JsonElement? ParseJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(value); }
        catch (JsonException) { throw new InvalidOperationException("INVALID_MEDIA_CONTEXT_SOURCE"); }
    }
}
