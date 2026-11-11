using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class SubscriptionPlan : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public PlanType PlanType { get; set; } = PlanType.Legacy;
    public PlanAudience Audience { get; set; } = PlanAudience.User;
    public int PriceVnd { get; set; }
    public int AiCredits { get; set; }
    /// <summary>Số lượt gửi duyệt của gói (chỉ Gói Creator).</summary>
    public int SubmitCredits { get; set; }
    public bool IsActive { get; set; } = true;
}
