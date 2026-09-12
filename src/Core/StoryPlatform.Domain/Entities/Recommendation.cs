using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class Recommendation : BaseEntity
{
    public int LearningInsightId { get; set; }
    public virtual LearningInsight? LearningInsight { get; set; }

    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public RecommendationCategory Category { get; set; }
    public string CurrentState { get; set; } = string.Empty;
    public string ProposedChange { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public RecommendationStatus Status { get; set; } = RecommendationStatus.RecommendationCreated;
}
