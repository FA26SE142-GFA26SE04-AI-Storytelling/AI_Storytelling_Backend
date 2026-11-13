using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>Một lượt báo cáo đánh giá truyện. Danh tính người báo cáo không bao giờ trả cho người viết hay Creator.</summary>
public class StoryRatingReport : BaseEntity
{
    public int StoryRatingId { get; set; }
    public virtual StoryRating? StoryRating { get; set; }

    public int ReporterUserId { get; set; }
    public virtual UserAccount? ReporterUser { get; set; }

    public StoryRatingReportReason Reason { get; set; }

    /// <summary>Đã che số điện thoại / email.</summary>
    public string? Note { get; set; }

    /// <summary>Mục hàng đợi mà báo cáo được gộp vào.</summary>
    public int? ModerationItemId { get; set; }
    public virtual ModerationItem? ModerationItem { get; set; }

    public StoryRatingReportStatus Status { get; set; } = StoryRatingReportStatus.Open;
    public DateTime? ResolvedAt { get; set; }
}
