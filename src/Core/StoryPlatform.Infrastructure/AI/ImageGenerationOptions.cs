namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Transport-level options for the image generation HTTP call to Gemini / Vertex AI.
/// Business retry counts live in Application-layer MediaGenerationOptions.
/// </summary>
public sealed class ImageGenerationOptions
{
    public const string SectionName = "AI:ImageGeneration";

    public string Provider { get; set; } = "VertexAI";
    public string Model { get; set; } = "gemini-3.1-flash-image";
    public string AspectRatio { get; set; } = "16:9";
    public string SafetySetting { get; set; } = "block_some";
    public string PersonGeneration { get; set; } = "allow_adult";
    public int NumberOfImages { get; set; } = 1;
    public int TimeoutSeconds { get; set; } = 60;
    public int TransportRetryCount { get; set; } = 3;
    public int TransportRetryBaseDelayMs { get; set; } = 500;
}
