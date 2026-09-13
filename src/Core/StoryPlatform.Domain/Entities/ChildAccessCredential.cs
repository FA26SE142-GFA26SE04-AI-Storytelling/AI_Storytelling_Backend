using System;

namespace StoryPlatform.Domain.Entities;

public class ChildAccessCredential : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public string AvatarId { get; set; } = string.Empty;
    public string PinHash { get; set; } = string.Empty;
    public int FailedAttempts { get; set; } = 0;
    public DateTime? LockedUntil { get; set; }

    public int CreatedByUserId { get; set; }
    public virtual UserAccount? CreatedByUser { get; set; }
}
