using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class SupervisionPermission : BaseEntity
{
    public int SupervisionRelationshipId { get; set; }
    public virtual SupervisionRelationship? SupervisionRelationship { get; set; }

    public Permission Permission { get; set; }
}
