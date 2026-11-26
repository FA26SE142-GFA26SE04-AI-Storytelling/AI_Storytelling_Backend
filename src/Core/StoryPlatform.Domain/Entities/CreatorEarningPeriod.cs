namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Một kỳ chia tiền đã chốt (Luồng 5, 5f bước 6 và 5h bước 2): vào ngày chốt Admin đặt (mặc định ngày 8 tháng sau, sau 7 ngày soát
/// xét), thu nhập của tháng trước được cộng vào số dư của từng Creator. Admin cũng có thể bấm Chốt kỳ ngay để chốt đến hết hôm nay
/// (dùng khi demo); khi đó một tháng có thể gồm nhiều kỳ. Các kỳ nối tiếp nhau, không chồng ngày, nên chốt lại không cộng trùng.
/// </summary>
public class CreatorEarningPeriod : BaseEntity
{
    /// <summary>Ngày 1 của tháng chứa ngày đầu kỳ.</summary>
    public DateTime PeriodMonth { get; set; }

    /// <summary>Ngày đầu của kỳ (theo ngày dương lịch, cùng cách tính kỳ của thống kê thu nhập).</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>Ngày cuối của kỳ (tính cả ngày này).</summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>Quỹ Creator của kỳ tại lúc chốt.</summary>
    public long CreatorFundVnd { get; set; }

    /// <summary>Tổng lượt đọc hợp lệ của mọi Creator trong kỳ tại lúc chốt.</summary>
    public int TotalValidReads { get; set; }

    /// <summary>Tổng thu nhập đã cộng cho các Creator (≤ quỹ vì làm tròn xuống).</summary>
    public long DistributedVnd { get; set; }

    public DateTime ClosedAt { get; set; }

    public virtual ICollection<CreatorBalanceEntry> Entries { get; set; } = new List<CreatorBalanceEntry>();
}
