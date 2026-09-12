using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class SharedStory : BaseEntity
{
    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int SharedByUserId { get; set; }
    public virtual UserAccount? SharedByUser { get; set; }

    public int ClassGroupId { get; set; }
    public virtual ClassGroup? ClassGroup { get; set; }

    public ShareMode ShareMode { get; set; }
    public TeacherShareStatus TeacherStatus { get; set; } = TeacherShareStatus.Pending;

    public int? ReviewedByUserId { get; set; }
    public virtual UserAccount? ReviewedByUser { get; set; }

    public DateTime? ReviewedAt { get; set; }
}
