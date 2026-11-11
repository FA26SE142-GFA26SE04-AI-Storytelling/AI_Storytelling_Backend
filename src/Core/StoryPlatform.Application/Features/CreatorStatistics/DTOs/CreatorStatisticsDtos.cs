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
