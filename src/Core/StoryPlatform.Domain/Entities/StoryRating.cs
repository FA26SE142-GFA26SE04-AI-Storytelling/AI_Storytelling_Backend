using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>Đánh giá (1–5 sao + nhận xét tùy chọn) của một phụ huynh cho một truyện Creator công khai.</summary>
public class StoryRating : BaseEntity
{
    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int ReviewerUserId { get; set; }
    public virtual UserAccount? ReviewerUser { get; set; }

    public int Rating { get; set; }

    /// <summary>Đã che số điện thoại / email trước khi lưu; null khi chỉ chấm sao.</summary>
    public string? Comment { get; set; }

    public StoryRatingStatus Status { get; set; } = StoryRatingStatus.Published;

    /// <summary>Lý do Moderator ẩn; người viết đọc được.</summary>
    public string? HiddenReason { get; set; }

    public int? ModeratedByUserId { get; set; }
    public virtual UserAccount? ModeratedByUser { get; set; }
    public DateTime? ModeratedAt { get; set; }
}
