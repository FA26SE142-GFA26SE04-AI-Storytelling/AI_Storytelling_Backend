using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class ChildProfileVersionHistory : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int? RecommendationId { get; set; }
    public virtual Recommendation? Recommendation { get; set; }

    public string PreviousConfig { get; set; } = string.Empty;
    public string NewConfig { get; set; } = string.Empty;

    public int? AppliedByUserId { get; set; }
    public virtual UserAccount? AppliedByUser { get; set; }

    public VersionHistoryStatus VersionStatus { get; set; } = VersionHistoryStatus.DraftVersion;
    public DateTime AppliedAt { get; set; }
}
