namespace StoryPlatform.Application.Features.StoryRatings.DTOs;

/// <summary>
/// Chi tiết mục kiểm duyệt đánh giá cho Moderator. Không có id, họ tên đầy đủ hay email
/// của người viết đánh giá và người báo cáo.
/// </summary>
public class StoryRatingModerationDetailDto
{
    public int ItemId { get; set; }
    public int RatingId { get; set; }
    public int StoryId { get; set; }
    public string? StoryTitle { get; set; }

    /// <summary>"Phụ huynh H***".</summary>
    public string ReviewerName { get; set; } = string.Empty;

    public int Rating { get; set; }
    public string? Comment { get; set; }

    /// <summary>Published (đang hiện, bị báo cáo) hoặc PendingModeration.</summary>
    public string RatingStatus { get; set; } = string.Empty;

    /// <summary>"Screening" (lọc tự động giữ lại) hoặc "Report" (bị báo cáo).</summary>
    public string HoldSource { get; set; } = string.Empty;

    /// <summary>LINK, BLOCKED_TERM hoặc LOW_RATING_WITH_TEXT khi HoldSource = Screening.</summary>
    public string? ScreeningReasonCode { get; set; }

    public List<StoryRatingReportItemDto> Reports { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Một báo cáo gắn với mục; không có danh tính người báo cáo.</summary>
public class StoryRatingReportItemDto
{
    public string Reason { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class HideStoryRatingRequestDto
{
    /// <summary>Bắt buộc, tối đa 1000 ký tự; người viết đánh giá đọc được lý do này.</summary>
    public string Reason { get; set; } = string.Empty;
}

public class StoryRatingDecisionResultDto
{
    public int ItemId { get; set; }
    public int RatingId { get; set; }

    /// <summary>"Approved" hoặc "Hidden".</summary>
    public string Decision { get; set; } = string.Empty;

    public string RatingStatus { get; set; } = string.Empty;
    public DateTime DecidedAt { get; set; }
}
