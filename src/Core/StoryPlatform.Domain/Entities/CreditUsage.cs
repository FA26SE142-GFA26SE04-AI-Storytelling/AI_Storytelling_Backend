using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Lịch sử trừ lượt: mỗi lần một lô bị trừ (yêu cầu tạo truyện AI hoặc Admin trừ) ghi một dòng.
/// Thống kê "lượt đã dùng trong kỳ" đọc từ đây; lượt được cấp đọc từ credit_lots.
/// </summary>
public class CreditUsage : BaseEntity
{
    public int UserId { get; set; }
    public virtual UserAccount? User { get; set; }

    public int LotId { get; set; }
    public virtual CreditLot? Lot { get; set; }

    public int Amount { get; set; }
    public CreditUsageReason Reason { get; set; }
}
