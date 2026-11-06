namespace StoryPlatform.Domain.Entities;

/// <summary>A child's immutable learning/media edition of a shared manual story version.</summary>
public sealed class ParentStoryPackage : BaseEntity
{
    public int StoryId { get; set; }
    public int StoryVersionId { get; set; }
    public int ChildProfileId { get; set; }
    public int CreatedByUserId { get; set; }
    public int Revision { get; set; } = 1;
    public int? PreviousPackageId { get; set; }
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
    public string Status { get; set; } = "PendingCheck";
    public string SourceFingerprint { get; set; } = "";
    public string ContextFingerprint { get; set; } = "";
    public string ContextSnapshotJson { get; set; } = "{}";
    public string ComponentsJson { get; set; } = "{}";
    public string GrayZoneAssessmentJson { get; set; } = "{}";
    public string CheckResultJson { get; set; } = "{}";
    public string ManifestJson { get; set; } = "{}";
    public string? LastErrorCode { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public int? ConfirmedByUserId { get; set; }
}
