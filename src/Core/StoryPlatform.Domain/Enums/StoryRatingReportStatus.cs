namespace StoryPlatform.Domain.Enums;

/// <summary>Kết quả xử lý một báo cáo đánh giá.</summary>
public enum StoryRatingReportStatus
{
    /// <summary>Đang chờ Moderator.</summary>
    Open = 1,
    /// <summary>Moderator giữ đánh giá.</summary>
    Approved = 2,
    /// <summary>Moderator ẩn đánh giá.</summary>
    Hidden = 3
}
