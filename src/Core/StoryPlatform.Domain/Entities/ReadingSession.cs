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

    // Đúng 1 trong 2 cột ChildAccessCredentialId / SupervisorSessionId có giá trị
    // (CHECK CK_reading_sessions_exactly_one_entry_source) — Luồng 3, Bước 3.0.
    public int? ChildAccessCredentialId { get; set; }
    public virtual ChildAccessCredential? ChildAccessCredential { get; set; }

    public int? SupervisorSessionId { get; set; }
    public virtual RefreshToken? SupervisorSession { get; set; }

    public DateTime? ForceExitRequestedAt { get; set; }
}
