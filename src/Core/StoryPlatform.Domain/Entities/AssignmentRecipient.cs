using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class AssignmentRecipient : BaseEntity
{
    public int AssignmentId { get; set; }
    public virtual Assignment? Assignment { get; set; }

    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public AssignmentStatus Status { get; set; } = AssignmentStatus.Assigned;
    public DateTime? CompletedAt { get; set; }
}
