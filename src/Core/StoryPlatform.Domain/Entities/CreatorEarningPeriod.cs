namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Một kỳ chia tiền (1 tháng dương lịch) đã chốt (Luồng 5, 5f bước 6 và 5h bước 2): ngày 8 tháng sau, sau 7 ngày soát
/// xét, thu nhập của kỳ được cộng vào số dư của từng Creator. Mỗi tháng một dòng nên chốt lại không cộng trùng.
/// </summary>
public class CreatorEarningPeriod : BaseEntity
{
    /// <summary>Ngày 1 của tháng (theo ngày dương lịch, cùng cách tính kỳ của thống kê thu nhập).</summary>
    public DateTime PeriodMonth { get; set; }

    /// <summary>Quỹ Creator của kỳ tại lúc chốt.</summary>
    public long CreatorFundVnd { get; set; }

    /// <summary>Tổng lượt đọc hợp lệ của mọi Creator trong kỳ tại lúc chốt.</summary>
    public int TotalValidReads { get; set; }

    /// <summary>Tổng thu nhập đã cộng cho các Creator (≤ quỹ vì làm tròn xuống).</summary>
    public long DistributedVnd { get; set; }

    public DateTime ClosedAt { get; set; }

    public virtual ICollection<CreatorBalanceEntry> Entries { get; set; } = new List<CreatorBalanceEntry>();
}
