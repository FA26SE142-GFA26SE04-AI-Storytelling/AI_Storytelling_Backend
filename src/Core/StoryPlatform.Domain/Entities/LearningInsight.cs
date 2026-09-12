using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class LearningInsight : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public InsightStatus Status { get; set; } = InsightStatus.InsightDetected;
    public string Observation { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
}
