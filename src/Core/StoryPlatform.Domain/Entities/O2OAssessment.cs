using System;

namespace StoryPlatform.Domain.Entities;

public class O2OAssessment : BaseEntity
{
    public int AssignmentId { get; set; }
    public virtual Assignment? Assignment { get; set; }

    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int TeacherUserId { get; set; }
    public virtual UserAccount? TeacherUser { get; set; }

    public int BonusPoints { get; set; } = 1;
    public string? Notes { get; set; }
    public DateTime AssessedAt { get; set; }
}
