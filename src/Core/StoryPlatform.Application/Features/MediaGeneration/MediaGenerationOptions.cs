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

    public string ImageProvider { get; set; } = "VertexAI";
    public string TtsProvider { get; set; } = "GeminiTTS";
    public string AlignmentEvaluator { get; set; } = "VertexAI";
    public string SafetyEvaluator { get; set; } = "VertexAI";
    public string LogLevel { get; set; } = "Summary";
    public MediaBranchOptions Images { get; set; } = new();
    public MediaBranchOptions Audio { get; set; } = new();

    public CircuitBreakerConfig CircuitBreaker { get; set; } = new();
    public RateLimitConfig RateLimit { get; set; } = new();
}

public sealed class MediaBranchOptions
{
    public int Concurrency { get; set; } = 1;
    public int ProviderTimeoutSeconds { get; set; } = 120;
    public CircuitBreakerConfig CircuitBreaker { get; set; } = new();
    public RateLimitConfig RateLimit { get; set; } = new();
}

public sealed class CircuitBreakerConfig
{
    public int FailureThreshold { get; set; } = 5;
    public double FailureRatio { get; set; } = 0.8;
    public int SamplingDurationSeconds { get; set; } = 60;
    public int DurationOfBreakSeconds { get; set; } = 300;
}

public sealed class RateLimitConfig
{
    public int RequestsPerMinute { get; set; } = 60;
    public int BurstSize { get; set; } = 10;
}
