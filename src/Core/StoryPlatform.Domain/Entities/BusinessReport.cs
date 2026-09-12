using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class BusinessReport : BaseEntity
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int StoriesGenerated { get; set; } = 0;
    public int StoriesApproved { get; set; } = 0;
    public int StoriesRejected { get; set; } = 0;
    public int ReadingSessionsCompleted { get; set; } = 0;
    public ReportStatus Status { get; set; } = ReportStatus.Compiling;
    public DateTime? PublishedAt { get; set; }
}
