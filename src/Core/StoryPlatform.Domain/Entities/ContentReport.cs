using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class ContentReport : BaseEntity
{
    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int ReporterUserId { get; set; }
    public virtual UserAccount? ReporterUser { get; set; }

    public ContentReportReason Reason { get; set; }
    public string? Description { get; set; }
    public ContentReportStatus Status { get; set; } = ContentReportStatus.Pending;

    public int? ReviewedByUserId { get; set; }
    public virtual UserAccount? ReviewedByUser { get; set; }
    public DateTime? ReviewedAt { get; set; }
}
