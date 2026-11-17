namespace StoryPlatform.Domain.Enums;

/// <summary>Loại bút toán trên số dư của Creator (Luồng 5, 5h).</summary>
public enum CreatorBalanceEntryType
{
    /// <summary>Thu nhập của một kỳ đã chốt (+).</summary>
    PeriodEarning = 1,
    /// <summary>Admin đã chuyển khoản ngoài hệ thống và đánh dấu Đã chi (−).</summary>
    Payout = 2,
    /// <summary>Admin huỷ một lần chi (chuyển nhầm, ngân hàng trả lại): cộng lại đúng số đã chi (+).</summary>
    PayoutReversal = 3
}
