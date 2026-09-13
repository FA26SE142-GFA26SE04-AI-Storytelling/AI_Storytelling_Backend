using System;

namespace StoryPlatform.Domain.Entities;

public class O2OAssessment : BaseEntity
{
    public int AssignmentRecipientId { get; set; }
    public virtual AssignmentRecipient? AssignmentRecipient { get; set; }

    public int TeacherUserId { get; set; }
    public virtual UserAccount? TeacherUser { get; set; }

    public int BonusPoints { get; set; } = 1;
    public string? Notes { get; set; }
    public DateTime AssessedAt { get; set; }
}
