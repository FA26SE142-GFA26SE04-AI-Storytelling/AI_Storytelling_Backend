using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class PaymentTransaction : BaseEntity
{
    public int PlanId { get; set; }
    public virtual SubscriptionPlan? Plan { get; set; }

    public int PayerUserId { get; set; }
    public virtual UserAccount? PayerUser { get; set; }

    public int? OrganizationId { get; set; }
    public virtual Organization? Organization { get; set; }

    public string TransactionCode { get; set; } = string.Empty;
    public int Amount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? SepayTransactionId { get; set; }
    public string? QrCodeUrl { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? PaidAt { get; set; }
}
