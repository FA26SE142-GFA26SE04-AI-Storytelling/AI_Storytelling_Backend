using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class OrgSafetyPolicyTemplate : BaseEntity
{
    public int OrganizationId { get; set; }
    public virtual Organization? Organization { get; set; }

    public int? MaxStoryLengthBaseline { get; set; }
    public ApprovalMode? RequiredApprovalModeDefault { get; set; }
}
