using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Một bút toán trên số dư của Creator (Luồng 5, 5h). Số dư = tổng <see cref="AmountVnd"/> của mọi bút toán.
/// Lần chi lưu lại mã giao dịch, ngày chuyển và thông tin ngân hàng tại lúc chi; huỷ lần chi ghi thêm một bút toán
/// <see cref="CreatorBalanceEntryType.PayoutReversal"/> chứ không sửa số tiền của lần chi.
/// </summary>
public class CreatorBalanceEntry : BaseEntity
{
    public int CreatorUserId { get; set; }
    public virtual UserAccount? Creator { get; set; }

    public CreatorBalanceEntryType Type { get; set; }

    /// <summary>Có dấu: thu nhập và huỷ lần chi là số dương, lần chi là số âm. Đơn vị đồng, trước thuế.</summary>
    public long AmountVnd { get; set; }

    /// <summary>Kỳ đã chốt (chỉ với <see cref="CreatorBalanceEntryType.PeriodEarning"/>).</summary>
    public int? EarningPeriodId { get; set; }
    public virtual CreatorEarningPeriod? EarningPeriod { get; set; }

    /// <summary>Lần chi bị huỷ (chỉ với <see cref="CreatorBalanceEntryType.PayoutReversal"/>).</summary>
    public int? ReversedEntryId { get; set; }
    public virtual CreatorBalanceEntry? ReversedEntry { get; set; }

    /// <summary>Lượt đọc hợp lệ của Creator trong kỳ (chỉ với thu nhập kỳ).</summary>
    public int? ValidReads { get; set; }

    // Lần chi (Payout)
    public string? BankTransactionCode { get; set; }
    public DateTime? TransferredOn { get; set; }
    public string? BankName { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountHolderName { get; set; }

    /// <summary>Admin ghi lần chi hoặc huỷ lần chi; null với thu nhập kỳ do hệ thống chốt.</summary>
    public int? RecordedByUserId { get; set; }

    /// <summary>Lý do huỷ (với bút toán huỷ).</summary>
    public string? Reason { get; set; }

    /// <summary>Thời điểm lần chi bị huỷ (đặt trên chính lần chi để lịch sử hiển thị trạng thái).</summary>
    public DateTime? CancelledAt { get; set; }
}
