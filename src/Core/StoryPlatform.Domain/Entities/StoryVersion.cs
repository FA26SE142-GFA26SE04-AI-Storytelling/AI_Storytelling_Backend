using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class StoryVersion : BaseEntity
{
    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int VersionNo { get; set; }
    public VersionEditType EditType { get; set; } = VersionEditType.Initial;

    public int? EditorUserId { get; set; }
    public virtual UserAccount? EditorUser { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? OutlineOpening { get; set; }
    public string? OutlineDevelopment { get; set; }
    public string? OutlineEnding { get; set; }
    public string? Content { get; set; }
    public string? Lesson { get; set; }
    public decimal? ReadabilityFkgl { get; set; }
    public decimal? ReadabilityFre { get; set; }
    public decimal? SafetyScore { get; set; }
    public bool IsCurrent { get; set; } = false;

    // Authoring provenance and command receipts on the shared version row.
    public string? AuthoringOrigin { get; set; }
    public string? AssistanceProvenanceJson { get; set; }
    public int? BaseVersionId { get; set; }
    public int? RestoredFromVersionId { get; set; }
    public int AudienceRevision { get; set; } = 1;
    public string SourceDeclaration { get; set; } = string.Empty;
    public string RightsDeclaration { get; set; } = string.Empty;
    public string? ImportFileName { get; set; }
    public string? ImportSourceHash { get; set; }
    public string? ImportPreviewHash { get; set; }
    public string? OperationId { get; set; }
    public string? RequestHash { get; set; }
    public string? OperationResponseJson { get; set; }

    public int? OutlineApprovedByUserId { get; set; }
    public virtual UserAccount? OutlineApprovedByUser { get; set; }
    public DateTime? OutlineApprovedAt { get; set; }

    public ModerationZone? ModerationZone { get; set; }
    public string? GrayZoneFlags { get; set; }
    public bool GrayZoneCleared { get; set; } = false;
    public int? GrayZoneClearedByUserId { get; set; }
    public virtual UserAccount? GrayZoneClearedByUser { get; set; }
    public DateTime? GrayZoneClearedAt { get; set; }

    public VersionReviewStatus ReviewStatus { get; set; } = VersionReviewStatus.None;
    public DateTime? ReviewSubmittedAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    public virtual UserAccount? ReviewedByUser { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewFeedback { get; set; }
}
