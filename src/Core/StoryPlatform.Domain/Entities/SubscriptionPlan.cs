using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class SubscriptionPlan : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public int PriceVnd { get; set; }
    public int QuotaAmount { get; set; }
    public bool IsActive { get; set; } = true;
}
