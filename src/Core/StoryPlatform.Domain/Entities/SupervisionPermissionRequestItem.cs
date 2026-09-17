using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class SupervisionPermissionRequestItem : BaseEntity
{
    public int SupervisionPermissionRequestId { get; set; }
    public virtual SupervisionPermissionRequest? SupervisionPermissionRequest { get; set; }

    public Permission Permission { get; set; }
}
