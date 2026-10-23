using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>Một điều ước của trẻ (WF-L2D).</summary>
public class StoryWish : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public StoryWishKind Kind { get; set; } = StoryWishKind.Wish;
    public StoryWishStatus Status { get; set; } = StoryWishStatus.Draft;

    public int? CharacterCardId { get; set; }
    public virtual WishCard? CharacterCard { get; set; }
    public int? PlaceCardId { get; set; }
    public virtual WishCard? PlaceCard { get; set; }
    public int? ChallengeCardId { get; set; }
    public virtual WishCard? ChallengeCard { get; set; }
    public int? LessonCardId { get; set; }
    public virtual WishCard? LessonCard { get; set; }
    public bool LessonLeftToParent { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public DateTime? PendingSince { get; set; }
    public int? FulfilledStoryId { get; set; }
    public virtual Story? FulfilledStory { get; set; }
    public bool FulfilledWithExistingStory { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
