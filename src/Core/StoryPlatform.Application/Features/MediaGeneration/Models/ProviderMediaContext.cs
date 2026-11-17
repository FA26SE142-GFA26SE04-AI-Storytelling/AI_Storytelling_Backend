using System.Text.Json;

namespace StoryPlatform.Application.Features.MediaGeneration.Models;

public sealed record ProviderMediaContext(
    int SchemaVersion,
    ProviderStoryFacts StoryFacts,
    ProviderVisualDesign VisualDesign);

public sealed record ProviderStoryFacts(
    string Title,
    string? Lesson,
    ProviderStoryOutline? ApprovedOutline,
    string CanonicalContent);

public sealed record ProviderStoryOutline(
    string? OutlineOpening,
    string? OutlineDevelopment,
    string? OutlineEnding);

public sealed record ProviderVisualDesign(
    string Style,
    string ContinuityRule,
    IReadOnlyList<string> ProhibitedChanges)
{
    public const string DefaultStyle = "child-friendly storybook illustration";
    public const string DefaultContinuityRule =
        "Keep character identity, clothing, important objects, locations and timeline consistent.";
    public static readonly IReadOnlyList<string> DefaultProhibitedChanges =
        ["plot", "lesson", "character identity", "canonical scene text"];

    public static ProviderVisualDesign CreateDefault() =>
        new(DefaultStyle, DefaultContinuityRule, DefaultProhibitedChanges);
}

public static class ProviderMediaContextSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Validate(ProviderMediaContext? context)
    {
        if (context is null ||
            context.SchemaVersion != 2 ||
            context.StoryFacts is null ||
            string.IsNullOrWhiteSpace(context.StoryFacts.Title) ||
            string.IsNullOrWhiteSpace(context.StoryFacts.CanonicalContent) ||
            context.VisualDesign is null ||
            string.IsNullOrWhiteSpace(context.VisualDesign.Style) ||
            string.IsNullOrWhiteSpace(context.VisualDesign.ContinuityRule) ||
            context.VisualDesign.ProhibitedChanges is null ||
            context.VisualDesign.ProhibitedChanges.Any(string.IsNullOrWhiteSpace))
        {
            throw new MediaProviderContextException(MediaProviderContextInvalidReason.OutputContractViolation);
        }
    }

    public static string Serialize(ProviderMediaContext context)
    {
        Validate(context);
        return JsonSerializer.Serialize(context, JsonOptions);
    }
}
