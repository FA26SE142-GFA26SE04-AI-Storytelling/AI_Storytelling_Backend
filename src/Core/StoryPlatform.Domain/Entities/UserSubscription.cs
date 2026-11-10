using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class UserSubscription : BaseEntity
{
    public int UserId { get; set; }
    public virtual UserAccount? User { get; set; }

    public int PlanId { get; set; }
    public virtual SubscriptionPlan? Plan { get; set; }

    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

    public int PaymentId { get; set; }
    public virtual PaymentTransaction? Payment { get; set; }

    public DateTime? RenewalReminderSentAt { get; set; }
}
