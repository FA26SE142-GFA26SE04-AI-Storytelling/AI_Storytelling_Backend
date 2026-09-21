namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Options for Gemini multimodal alignment/safety evaluation runs.
/// Independent from image-generation model: evaluation should be cheap and stable.
/// </summary>
public sealed class MediaEvaluationOptions
{
    public const string SectionName = "AI:MediaEvaluation";

    public string Model { get; set; } = "gemini-2.5-flash";
    public int TransportRetryCount { get; set; } = 2;
}
