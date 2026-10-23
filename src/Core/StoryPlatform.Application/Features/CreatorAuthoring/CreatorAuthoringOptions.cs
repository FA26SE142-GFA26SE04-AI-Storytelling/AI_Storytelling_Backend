namespace StoryPlatform.Application.Features.CreatorAuthoring;

/// <summary>Content limits and supported audience settings for Story authoring.</summary>
public sealed class CreatorAuthoringOptions
{
    public const string SectionName = "CreatorAuthoring";
    public int MaximumContentCharacters { get; set; } = 200_000;
    public int MaximumImportBytes { get; set; } = 5_000_000;
    public int ImportPreviewLifetimeHours { get; set; } = 24;
    public string[] SupportedLanguages { get; set; } = ["vi", "en"];
    public string[] SupportedAgeBands { get; set; } = ["6-8", "9-10"];
    public int GenerationMaximumLength { get; set; } = 1800;
    public decimal GenerationSafetyScoreThreshold { get; set; } = 90m;
}
