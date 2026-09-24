namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Options for Gemini multimodal alignment/safety evaluation and scene segmentation runs.
/// Uses gemini-3.8-flash for robust reasoning and multilingual verification.
/// </summary>
public sealed class MediaEvaluationOptions
{
    public const string SectionName = "AI:MediaEvaluation";

    public string Provider { get; set; } = "VertexAI";
    public string Model { get; set; } = "gemini-3.8-flash";
    public int TimeoutSeconds { get; set; } = 45;
    public int TransportRetryCount { get; set; } = 2;
    public int TransportRetryBaseDelayMs { get; set; } = 500;
}
