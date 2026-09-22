namespace StoryPlatform.Domain.Enums;

/// <summary>
/// Lifecycle status of a generated media asset.
///
/// IMPORTANT (Phase 5 plan revision round-2): the numeric values of the original four
/// states MUST be preserved to keep existing persisted data (if any) semantically
/// unchanged. The new Phase 5 states are appended with fresh numeric values:
///   Generated   = 5 — asset binary produced, not yet passed evaluation
///   ManualReview = 6 — business attempts exhausted, requires supervisor review
/// </summary>
public enum MediaStatus
{
    Queued = 1,
    Processing = 2,
    Ready = 3,
    Failed = 4,

    Generated = 5,
    ManualReview = 6
}
