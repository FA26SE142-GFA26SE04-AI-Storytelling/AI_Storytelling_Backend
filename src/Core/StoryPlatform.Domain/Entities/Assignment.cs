using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

// TODO: CHECK constraint — DBML note: class_group_id and child_profile_id must be mutually exclusive; requires raw SQL migration.
public class Assignment : BaseEntity
{
    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int AssignedByUserId { get; set; }
    public virtual UserAccount? AssignedByUser { get; set; }

    public int? ClassGroupId { get; set; }
    public virtual ClassGroup? ClassGroup { get; set; }

    public int? ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public AssignmentStatus Status { get; set; } = AssignmentStatus.Assigned;
    public DateTime AssignedAt { get; set; }
    public DateTime? DueAt { get; set; }
}
