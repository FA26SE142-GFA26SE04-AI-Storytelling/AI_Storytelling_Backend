using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class SupervisionInvitation : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int InviterUserId { get; set; }
    public virtual UserAccount? InviterUser { get; set; }

    public string? InvitationCode { get; set; }
    public string? InviteeEmail { get; set; }

    public int? InviteeUserId { get; set; }
    public virtual UserAccount? InviteeUser { get; set; }

    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
}
