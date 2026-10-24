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

    /// <summary>Truyện mới đang được sinh cho điều ước (phụ huynh "Tạo truyện mới").</summary>
    public int? GeneratedStoryId { get; set; }
    public virtual Story? GeneratedStory { get; set; }

    /// <summary>Yêu cầu sinh truyện (StoryGenerationRequest) tương ứng, để truy vết.</summary>
    public int? GenerationRequestId { get; set; }

    public int? ResolvedByUserId { get; set; }

    /// <summary>Lý do từ chối do phụ huynh ghi; chỉ phụ huynh thấy, không bao giờ trả cho trẻ.</summary>
    public string? DeclineReason { get; set; }

    /// <summary>Ghi chú khi hệ thống tự đóng: expired, story_archived, story_rejected, parent_cancelled, profile_inactive.</summary>
    public string? ResolutionNote { get; set; }

    /// <summary>Đã gửi story_wish_expiring (ngày 6) cho lần chờ hiện tại.</summary>
    public DateTime? ExpiryWarnedAt { get; set; }
}
