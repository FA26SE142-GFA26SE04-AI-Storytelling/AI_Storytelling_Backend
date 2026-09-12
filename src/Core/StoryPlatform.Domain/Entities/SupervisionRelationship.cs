using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class SupervisionRelationship : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int SupervisorUserId { get; set; }
    public virtual UserAccount? SupervisorUser { get; set; }

    public int? SupervisionInvitationId { get; set; }
    public virtual SupervisionInvitation? SupervisionInvitation { get; set; }

    public SupervisorRole SupervisorRole { get; set; }
    public DateTime? RevokedAt { get; set; }

    public int? RevokedByUserId { get; set; }
    public virtual UserAccount? RevokedByUser { get; set; }
}
