using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Hàng đợi kiểm duyệt chung (S4 Mục 5). Mỗi dòng là một việc Moderator nhận và quyết định.
/// </summary>
public class ModerationItem : BaseEntity
{
    public ModerationItemType ItemType { get; set; }
    public ModerationItemStatus Status { get; set; } = ModerationItemStatus.Pending;
    public ModerationPriority Priority { get; set; } = ModerationPriority.Medium;

    public int? StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int? StoryVersionId { get; set; }
    public virtual StoryVersion? StoryVersion { get; set; }

    public int? ContentReportId { get; set; }
    public virtual ContentReport? ContentReport { get; set; }

    public int? ReadingRoadmapId { get; set; }
    public virtual ReadingRoadmap? ReadingRoadmap { get; set; }

    public int? CreatedByUserId { get; set; }
    public virtual UserAccount? CreatedByUser { get; set; }

    public int? SourceChildProfileId { get; set; }
    public virtual ChildProfile? SourceChildProfile { get; set; }

    public CopyrightDeclaration? CopyrightDeclaration { get; set; }
    public string? PiiCheckResult { get; set; }

    public bool IsResubmission { get; set; }
    public int? PreviousItemId { get; set; }
    public virtual ModerationItem? PreviousItem { get; set; }

    public int? ClaimedByUserId { get; set; }
    public virtual UserAccount? ClaimedByUser { get; set; }
    public DateTime? ClaimedAt { get; set; }

    public DateTime? SlaDueAt { get; set; }
    public DateTime? SlaWarnedAt { get; set; }

    public string? Flags { get; set; }
    public string? Checklist { get; set; }
    public string? ReviewProgress { get; set; }
    public string? Feedback { get; set; }

    public string? DecisionReason { get; set; }
    public string? EscalationReason { get; set; }
    public int? DecidedByUserId { get; set; }
    public virtual UserAccount? DecidedByUser { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime? EscalatedAt { get; set; }

    public string? ClosedReason { get; set; }
    public DateTime? WithdrawnAt { get; set; }
    public DateTime? RemovedAt { get; set; }

    /// <summary>Khóa lạc quan: dịch vụ tăng 1 mỗi lần nhận / quyết định để hai Moderator không nhận trùng.</summary>
    public int RowVersion { get; set; }
}
