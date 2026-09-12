using System;

namespace StoryPlatform.Domain.Entities;

public class ClassGroupMember : BaseEntity
{
    public int ClassGroupId { get; set; }
    public virtual ClassGroup? ClassGroup { get; set; }

    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public DateTime JoinedAt { get; set; }
}
