using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class OrganizationMembership : BaseEntity
{
    public int OrganizationId { get; set; }
    public virtual Organization? Organization { get; set; }

    public int UserId { get; set; }
    public virtual UserAccount? User { get; set; }

    public OrgRole OrgRole { get; set; }
    public MembershipStatus Status { get; set; } = MembershipStatus.Pending;

    public int? InvitedByUserId { get; set; }
    public virtual UserAccount? InvitedByUser { get; set; }
}
