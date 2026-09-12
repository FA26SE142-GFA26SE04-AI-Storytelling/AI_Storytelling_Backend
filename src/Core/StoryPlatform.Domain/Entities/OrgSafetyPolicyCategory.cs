using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class OrgSafetyPolicyCategory : BaseEntity
{
    public int OrgSafetyPolicyTemplateId { get; set; }
    public virtual OrgSafetyPolicyTemplate? OrgSafetyPolicyTemplate { get; set; }

    public int ContentCategoryId { get; set; }
    public virtual ContentCategory? ContentCategory { get; set; }

    public PolicyRule Rule { get; set; }
}
