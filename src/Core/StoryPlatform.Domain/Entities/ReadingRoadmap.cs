using System;
using System.Collections.Generic;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Bộ truyện mẫu (WF-L5I). Published bất biến; muốn chỉnh thì nhân bản thành bản Draft mới.
/// </summary>
public class ReadingRoadmap : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AgeBand AgeBand { get; set; }

    public int MainCategoryId { get; set; }
    public virtual ContentCategory? MainCategory { get; set; }

    public int ReadingLevelMin { get; set; }
    public int ReadingLevelMax { get; set; }
    public int DefaultDaysPerStory { get; set; } = 1;
    public RoadmapTemplateStatus Status { get; set; } = RoadmapTemplateStatus.Draft;

    public int CreatedByUserId { get; set; }
    public virtual UserAccount? CreatedByUser { get; set; }

    public DateTime? SubmittedAt { get; set; }

    public int? PublishedByUserId { get; set; }
    public virtual UserAccount? PublishedByUser { get; set; }
    public DateTime? PublishedAt { get; set; }

    public int? ClonedFromId { get; set; }
    public virtual ReadingRoadmap? ClonedFrom { get; set; }

    public RoadmapArchiveReason? ArchivedReason { get; set; }
    public int? ArchivedByUserId { get; set; }
    public virtual UserAccount? ArchivedByUser { get; set; }
    public string? ArchiveNote { get; set; }
    public DateTime? ArchivedAt { get; set; }

    public virtual ICollection<ReadingRoadmapItem> Items { get; set; } = new List<ReadingRoadmapItem>();
}
