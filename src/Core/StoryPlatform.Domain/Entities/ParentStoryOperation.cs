namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Immutable receipt/snapshot of a family authoring decision. Check, content confirmation,
/// preparation and family grant are separate kinds, linked by IDs in their typed result.
/// A FamilyConfirmation grants only its pinned preparation; IsCurrent never grants access.
/// </summary>
public sealed class ParentStoryOperation : BaseEntity
{
    public int StoryId { get; set; }
    public int ActorUserId { get; set; }
    public int ChildProfileId { get; set; }
    public int? StoryVersionId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ContextFingerprint { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public DateTime? RevokedAt { get; set; }
}
