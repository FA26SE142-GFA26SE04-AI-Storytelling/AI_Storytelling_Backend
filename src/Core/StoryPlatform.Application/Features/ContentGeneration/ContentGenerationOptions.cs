namespace StoryPlatform.Application.Features.ContentGeneration;

public sealed class ContentGenerationOptions
{
    public const string SectionName = "AI:ContentGeneration";
    public int MaxContentRefinementAttempts { get; set; } = 2;
    public int ArtifactMaxAttempts { get; set; } = 2;
    public int JobLeaseMinutes { get; set; } = 4;
    public int ContentExecutionBudgetSeconds { get; set; } = 180;
    /// <summary>Từ chấm điểm bị chặn trong tương tác lúc nghe; null hoặc rỗng dùng mặc định.</summary>
    public string[]? ListeningGradingWords { get; set; }
}
