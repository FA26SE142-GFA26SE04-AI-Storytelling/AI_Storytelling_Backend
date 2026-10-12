using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class OwnershipTransferRequest : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int CurrentOwnerUserId { get; set; }
    public virtual UserAccount? CurrentOwnerUser { get; set; }

    public int TargetSupervisorUserId { get; set; }
    public virtual UserAccount? TargetSupervisorUser { get; set; }

    public OwnershipTransferRequestStatus Status { get; set; } = OwnershipTransferRequestStatus.Pending;
    public DateTime? RespondedAt { get; set; }
}
