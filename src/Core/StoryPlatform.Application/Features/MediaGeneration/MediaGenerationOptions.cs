namespace StoryPlatform.Application.Features.MediaGeneration;

public sealed class MediaGenerationOptions
{
    public const string SectionName = "AI:MediaGeneration";
    public int JobLeaseMinutes { get; set; } = 5;
    public int AssetMaxAttempts { get; set; } = 3;
    public bool WorkerEnabled { get; set; }
    public int IdleDelaySeconds { get; set; } = 2;
    public int TransientFailureDelaySeconds { get; set; } = 2;
    public int PermanentFailureDelaySeconds { get; set; } = 300;
}
