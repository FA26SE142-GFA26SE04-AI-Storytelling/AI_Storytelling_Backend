using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class SupervisionPermissionRequest : BaseEntity
{
    public int SupervisionRelationshipId { get; set; }
    public virtual SupervisionRelationship? SupervisionRelationship { get; set; }

    public int RequesterUserId { get; set; }
    public virtual UserAccount? RequesterUser { get; set; }

    public PermissionRequestStatus Status { get; set; } = PermissionRequestStatus.Pending;
    public DateTime? RespondedAt { get; set; }

    public int? RespondedByUserId { get; set; }
    public virtual UserAccount? RespondedByUser { get; set; }

    public virtual ICollection<SupervisionPermissionRequestItem> Items { get; set; }
        = new List<SupervisionPermissionRequestItem>();
}
