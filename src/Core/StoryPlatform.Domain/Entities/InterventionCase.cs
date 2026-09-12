using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class InterventionCase : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int? RecommendationId { get; set; }
    public virtual Recommendation? Recommendation { get; set; }

    public InterventionTrigger TriggerType { get; set; }
    public InterventionStatus Status { get; set; } = InterventionStatus.OpenHoldMode;
    public string? SkillGapNotes { get; set; }
    public DateTime OpenedAt { get; set; }

    public int? ResolvedByUserId { get; set; }
    public virtual UserAccount? ResolvedByUser { get; set; }

    public DateTime? ResolvedAt { get; set; }
}
