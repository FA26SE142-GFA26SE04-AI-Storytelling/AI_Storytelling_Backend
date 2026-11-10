using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class SubscriptionPlan : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public PlanType PlanType { get; set; } = PlanType.Legacy;
    public int PriceVnd { get; set; }
    public int AiCredits { get; set; }
    public bool IsActive { get; set; } = true;
}
