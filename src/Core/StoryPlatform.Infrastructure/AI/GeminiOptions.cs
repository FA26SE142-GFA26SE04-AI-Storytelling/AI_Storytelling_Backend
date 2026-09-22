namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Shared options for outbound Gemini REST v1 calls.
/// Used by image generation, media alignment/safety evaluation, semantic scene segmentation,
/// and media context extraction providers.
/// </summary>
public sealed class GeminiOptions
{
    public const string SectionName = "AI:Gemini";

    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1/models";
    public int TimeoutSeconds { get; set; } = 150;
    public int TransportRetryCount { get; set; } = 3;
    public int TransportRetryBaseDelayMs { get; set; } = 500;
}
