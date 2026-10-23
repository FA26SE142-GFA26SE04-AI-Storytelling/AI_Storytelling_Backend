using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>Durable, independently leased work for one illustration asset.</summary>
public class MediaAssetGenerationTask : BaseEntity
{
    public int StoryId { get; set; }
    public int StoryVersionId { get; set; }
    public int BranchJobId { get; set; }
    public int MediaAssetId { get; set; }
    public int IllustrationBeatId { get; set; }
    public int MediaContextId { get; set; }
    public int PackageRevision { get; set; }
    public GenerationJobStatus Status { get; set; } = GenerationJobStatus.Pending;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
    public string? ErrorCode { get; set; }
    public int AttemptNo { get; set; }
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
}
