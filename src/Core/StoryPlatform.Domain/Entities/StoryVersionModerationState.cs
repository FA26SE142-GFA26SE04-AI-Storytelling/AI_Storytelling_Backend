using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>Current moderation projection. Moderator work and decision history remain in ModerationItem.</summary>
public sealed class StoryVersionModerationState
{
    public int StoryVersionId { get; set; }
    public ModerationZone? ModerationZone { get; set; }
    public string? GrayZoneFlags { get; set; }
    public bool GrayZoneCleared { get; set; }
    public int? GrayZoneClearedByUserId { get; set; }
    public UserAccount? GrayZoneClearedByUser { get; set; }
    public DateTime? GrayZoneClearedAt { get; set; }
    public VersionReviewStatus ReviewStatus { get; set; } = VersionReviewStatus.None;
    public DateTime? ReviewSubmittedAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    public UserAccount? ReviewedByUser { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewFeedback { get; set; }
}
