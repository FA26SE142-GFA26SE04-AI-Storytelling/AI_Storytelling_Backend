using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class CreditLot : BaseEntity
{
    public int UserId { get; set; }
    public virtual UserAccount? User { get; set; }

    public CreditType CreditType { get; set; } = CreditType.AI;
    public CreditSource Source { get; set; }
    public int Total { get; set; }
    public int Remaining { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public int? PaymentId { get; set; }
    public virtual PaymentTransaction? Payment { get; set; }

    /// <summary>Khóa phạm vi Event khi Source == EventGrant (EV-12); null với các lô thông thường.</summary>
    public int? EventId { get; set; }
}
