using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Generated binary asset (image or audio) attached to a StoryVersion.
///
/// Phase 5 extensions:
///   - StorySegmentId is set for TTS audio (1 asset per StorySegment).
///   - ValidationStatus separates "binary produced" (MediaStatus.Generated) from
///     "passed alignment + safety checks" (ValidationStatus.Passed → MediaStatus.Ready).
///   - Provider/Model/ProviderRequestId/ProviderAssetId trace provenance for audit + retry.
///   - AttemptCount tracks BUSINESS-level regeneration (excludes transport retries).
/// </summary>
public class MediaAsset : BaseEntity
{
    public int StoryVersionId { get; set; }
    public virtual StoryVersion? StoryVersion { get; set; }

    public int? StorySceneId { get; set; }
    public virtual StoryScene? StoryScene { get; set; }

    public int? StorySegmentId { get; set; }
    public virtual StorySegment? StorySegment { get; set; }

    public MediaType Type { get; set; }
    public MediaStatus Status { get; set; } = MediaStatus.Queued;
    public ValidationStatus ValidationStatus { get; set; } = ValidationStatus.Pending;

    public int? SceneIndex { get; set; }
    public string? Url { get; set; }
    public string? WordTimings { get; set; }
    public string? MimeType { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? ProviderRequestId { get; set; }
    public string? ProviderAssetId { get; set; }
    public string? ValidationResultJson { get; set; }
    public int? ReferenceImageAssetId { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? LastValidationReason { get; set; }
}
