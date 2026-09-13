using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class ReadingSession : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int? AssignmentRecipientId { get; set; }
    public virtual AssignmentRecipient? AssignmentRecipient { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Started;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int TimeSpentSeconds { get; set; } = 0;
    public int PagesCompleted { get; set; } = 0;

    public int? StoryVersionId { get; set; }
    public virtual StoryVersion? StoryVersion { get; set; }

    public int? ChildAccessCredentialId { get; set; }
    public virtual ChildAccessCredential? ChildAccessCredential { get; set; }

    public int? SupervisorSessionId { get; set; }
    public virtual RefreshToken? SupervisorSession { get; set; }

    public DateTime? ForceExitRequestedAt { get; set; }
}
