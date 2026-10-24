using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class LearningArtifactGenerationBatch : BaseEntity
{
    public int StoryId { get; set; }
    public int StoryVersionId { get; set; }
    public int RootJobId { get; set; }
    public int RequestedByUserId { get; set; }
    public string SourceFingerprint { get; set; } = string.Empty;
    public string ContextFingerprint { get; set; } = string.Empty;
    public string? RootClaimToken { get; set; }
    public string Kind { get; set; } = string.Empty;
    public int BatchNumber { get; set; }
    public int TargetCount { get; set; }
    public string RequestJson { get; set; } = string.Empty;
    public string? ResultJson { get; set; }
    public string? ShortfallReason { get; set; }
    public GenerationJobStatus Status { get; set; } = GenerationJobStatus.Pending;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
    public int AttemptCount { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime? CompletedAt { get; set; }
}
