using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Giao dịch đã Paid nhưng chưa được áp gói, chờ Admin xử lý (UC-L2-13). Mỗi giao dịch tối đa một dòng.
/// </summary>
public class PaymentReconciliationException : BaseEntity
{
    public int PaymentId { get; set; }
    public virtual PaymentTransaction? Payment { get; set; }

    public ReconciliationStatus Status { get; set; } = ReconciliationStatus.Open;
    public string Reason { get; set; } = string.Empty;

    public int? ResolvedBy { get; set; }
    public virtual UserAccount? ResolvedByUser { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
