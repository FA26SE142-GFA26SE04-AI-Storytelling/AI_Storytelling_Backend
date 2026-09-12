using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class OrganizationPermission : BaseEntity
{
    public int OrganizationMembershipId { get; set; }
    public virtual OrganizationMembership? OrganizationMembership { get; set; }

    public OrgPermission Permission { get; set; }

    public int? GrantedByUserId { get; set; }
    public virtual UserAccount? GrantedByUser { get; set; }
}
