namespace StoryPlatform.Application.Features.CreatorStatistics.DTOs;

/// <summary>
/// Thống kê lượt đọc truyện của một Creator trong một kỳ (F-21). Chỉ có số tổng hợp, không có dữ liệu của trẻ.
/// </summary>
public class CreatorReadingStatisticsDto
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int Views { get; set; }
    public int Favorites { get; set; }
    public int CompletedReadings { get; set; }
    public int ValidReads { get; set; }
    public decimal CompletionRate { get; set; }
    public List<CreatorStoryReadingDto> Stories { get; set; } = [];
}

public class CreatorStoryReadingDto
{
    public int StoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>Số phiên đọc được mở trong kỳ.</summary>
    public int Views { get; set; }
    /// <summary>Số trẻ đang đánh dấu yêu thích truyện (số hiện tại, không theo kỳ).</summary>
    public int Favorites { get; set; }
    /// <summary>Trong các phiên mở trong kỳ, số phiên đã đọc hết.</summary>
    public int CompletedReadings { get; set; }
    /// <summary>Lượt đọc hợp lệ của kỳ, là đầu vào chia quỹ Creator.</summary>
    public int ValidReads { get; set; }
    /// <summary>CompletedReadings ÷ Views, từ 0 đến 1; bằng 0 khi chưa có lượt xem.</summary>
    public decimal CompletionRate { get; set; }
}

/// <summary>
/// Thu nhập dự kiến của một Creator từ quỹ Creator của một kỳ (F-20). Chi trả thực hiện ngoài hệ thống.
/// </summary>
public class CreatorRevenueStatisticsDto
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    /// <summary>Quỹ của kỳ = ReadingFundRate × doanh thu Gói Đọc + CreativeFundRate × doanh thu Gói Sáng tạo (theo PaidAt).</summary>
    public long CreatorFundVnd { get; set; }
    public decimal ReadingFundRate { get; set; }
    public decimal CreativeFundRate { get; set; }
    /// <summary>Thời gian đọc tối thiểu (giây) để một lượt đọc được tính là hợp lệ.</summary>
    public int MinimumReadSeconds { get; set; }

    /// <summary>Tổng lượt đọc hợp lệ của mọi Creator trong kỳ (mẫu số khi chia quỹ).</summary>
    public int TotalValidReadsAllCreators { get; set; }
    public int MyValidReads { get; set; }
    /// <summary>MyValidReads × CreatorFundVnd ÷ TotalValidReadsAllCreators, làm tròn xuống đến đồng.</summary>
    public long EstimatedEarningsVnd { get; set; }
    public List<CreatorStoryRevenueDto> Stories { get; set; } = [];
}

public class CreatorStoryRevenueDto
{
    public int StoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int ValidReads { get; set; }
    public long EstimatedEarningsVnd { get; set; }
}

/// <summary>
/// Thống kê nội dung của một Creator (F-22): số truyện theo trạng thái, kết quả gửi duyệt trong kỳ, lượt còn lại và
/// tiến độ tới hạng Creator đối tác. Chỉ có số tổng hợp của chính Creator, không có nội dung truyện hay dữ liệu của trẻ.
/// </summary>
public class CreatorContentStatisticsDto
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    /// <summary>Truyện chưa archive của Creator: Published, chờ duyệt, Ready (chưa gửi) và Nháp; mỗi truyện chỉ vào một nhóm.</summary>
    public int Draft { get; set; }
    public int Ready { get; set; }
    public int PendingReview { get; set; }
    public int Published { get; set; }

    /// <summary>Lượt gửi duyệt trong kỳ theo kết quả (Approved / Rejected / ChangesRequested tính theo ngày quyết định).</summary>
    public int SubmissionsApproved { get; set; }
    public int SubmissionsRejected { get; set; }
    public int SubmissionsChangesRequested { get; set; }
    /// <summary>Số lượt Creator tự rút trong kỳ (theo ngày rút).</summary>
    public int SubmissionsWithdrawn { get; set; }

    /// <summary>Tổng lượt AI còn lại theo hạn mức Creator.</summary>
    public int RemainingAiCredits { get; set; }
    /// <summary>Tổng lượt gửi duyệt còn lại theo hạn mức Creator.</summary>
    public int RemainingSubmitCredits { get; set; }

    /// <summary>Hạng hiện tại (hiện luôn là "Creator"; chưa có việc nâng hạng đối tác).</summary>
    public string Tier { get; set; } = string.Empty;
    public PartnerProgressDto PartnerProgress { get; set; } = new();
}

/// <summary>Tiến độ tới hạng Creator đối tác trong cửa sổ 90 ngày gần nhất, độc lập với kỳ người dùng chọn.</summary>
public class PartnerProgressDto
{
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    /// <summary>True khi mọi tiêu chí đều đạt.</summary>
    public bool IsEligible { get; set; }
    public List<PartnerCriterionDto> Criteria { get; set; } = [];
}

public class PartnerCriterionDto
{
    /// <summary>published_stories, completion_rate, valid_reads hoặc no_violation_removals.</summary>
    public string Key { get; set; } = string.Empty;
    public decimal Current { get; set; }
    /// <summary>Ngưỡng cần đạt (tối thiểu); riêng no_violation_removals là mức tối đa cho phép.</summary>
    public decimal Target { get; set; }
    public bool Met { get; set; }
}
