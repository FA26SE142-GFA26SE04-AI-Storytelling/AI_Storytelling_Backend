using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class RecommendationReview : BaseEntity
{
    public int RecommendationId { get; set; }
    public virtual Recommendation? Recommendation { get; set; }

    public int ReviewerUserId { get; set; }
    public virtual UserAccount? ReviewerUser { get; set; }

    public ReviewDecision Decision { get; set; }
    public string? ModifiedValue { get; set; }
    public string? Reason { get; set; }
    public bool IsFinal { get; set; } = false;
    public bool HadFinalAuthority { get; set; } = false;
    public DateTime ReviewedAt { get; set; }
}
