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

    /// <summary>Mục kiểm duyệt đang giữ lượt gửi duyệt này; chuyển sang lần gửi lại sau khi bị yêu cầu sửa.</summary>
    public int? ModerationItemId { get; set; }
    public virtual ModerationItem? ModerationItem { get; set; }

    /// <summary>Thời điểm lượt được hoàn lại (bị từ chối hoặc rút khi còn chờ); null nếu chưa hoàn.</summary>
    public DateTime? RefundedAt { get; set; }

    /// <summary>
    /// Truyện đang giữ lượt AI này (Luồng 2, 2d). Lượt bị trừ khỏi lô ngay khi gửi yêu cầu (đang giữ);
    /// <see cref="ConsumedAt"/> ghi khi AI sinh xong nội dung, <see cref="RefundedAt"/> ghi khi trả lượt.
    /// </summary>
    public int? StoryId { get; set; }
    public virtual Story? Story { get; set; }

    /// <summary>Thời điểm lượt đang giữ được tính chính thức (AI sinh xong nội dung); null nếu còn giữ hoặc đã trả.</summary>
    public DateTime? ConsumedAt { get; set; }
}
