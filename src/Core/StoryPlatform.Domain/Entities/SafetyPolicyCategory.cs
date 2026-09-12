using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class SafetyPolicyCategory : BaseEntity
{
    public int SafetyPolicyId { get; set; }
    public virtual SafetyPolicy? SafetyPolicy { get; set; }

    public int ContentCategoryId { get; set; }
    public virtual ContentCategory? ContentCategory { get; set; }

    public PolicyRule Rule { get; set; }
}
