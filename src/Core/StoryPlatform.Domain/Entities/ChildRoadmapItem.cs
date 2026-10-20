using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Một truyện trong lộ trình của một trẻ (WF-L4A). Mỗi trẻ có đúng một lộ trình: các dòng cùng ChildProfileId.
/// Dòng không bị xóa khi gỡ hay bỏ qua, chỉ đổi trạng thái.
/// </summary>
public class ChildRoadmapItem : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    /// <summary>Thứ tự trong hàng chờ (nhỏ hơn đọc trước).</summary>
    public int Position { get; set; }

    public ChildRoadmapItemStatus Status { get; set; } = ChildRoadmapItemStatus.Waiting;
    public ChildRoadmapSkipReason? SkipReason { get; set; }
    public ChildRoadmapItemSource Source { get; set; } = ChildRoadmapItemSource.Manual;

    /// <summary>Bộ truyện mẫu mà dòng này được chép từ đó (chỉ để truy vết; bộ đổi sau không ảnh hưởng dòng).</summary>
    public int? SourceReadingRoadmapId { get; set; }
    public virtual ReadingRoadmap? SourceReadingRoadmap { get; set; }

    public int AddedByUserId { get; set; }
    public virtual UserAccount? AddedByUser { get; set; }

    /// <summary>Lời nhắn của bố mẹ cho con.</summary>
    public string? Note { get; set; }

    /// <summary>Số ngày mong con đọc xong, tính từ lúc mở.</summary>
    public int? ExpectedDays { get; set; }
    public DateTime? ExpectedCompletionAt { get; set; }

    public DateTime? OpenedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? SkippedAt { get; set; }
    public DateTime? RemovedAt { get; set; }

    public int? RemovedByUserId { get; set; }
    public virtual UserAccount? RemovedByUser { get; set; }
}
