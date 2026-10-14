using System;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Quyền đọc tạm 2 giờ do Moderator bấm "Xem để rà soát" ở tab ngoài hàng đợi.
/// Lý do và lượt mở vẫn ghi ở audit_logs.
/// </summary>
public class ModerationReviewView : BaseEntity
{
    public int ModeratorUserId { get; set; }
    public virtual UserAccount? ModeratorUser { get; set; }

    public int? StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int? StoryVersionId { get; set; }
    public virtual StoryVersion? StoryVersion { get; set; }

    public int? ReadingRoadmapId { get; set; }
    public virtual ReadingRoadmap? ReadingRoadmap { get; set; }

    /// <summary>"published_content" hoặc "roadmap_templates".</summary>
    public string SourceTab { get; set; } = string.Empty;

    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}
