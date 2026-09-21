namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Transport-level options for the image generation HTTP call to Gemini.
/// Business retry counts live in Application-layer MediaGenerationOptions.
/// </summary>
public sealed class ImageGenerationOptions
{
    public const string SectionName = "AI:ImageGeneration";

    public string Model { get; set; } = "gemini-2.5-flash-image";
    public string AspectRatio { get; set; } = "16:9";
    public int TransportRetryCount { get; set; } = 3;
    public int TransportRetryBaseDelayMs { get; set; } = 500;
}
