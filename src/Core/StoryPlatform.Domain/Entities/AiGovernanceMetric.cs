using System;

namespace StoryPlatform.Domain.Entities;

public class AiGovernanceMetric : BaseEntity
{
    public DateTime MetricDate { get; set; }
    public decimal? GenerationSuccessRate { get; set; }
    public decimal? SafetyFlagRate { get; set; }
    public decimal? RegenerationRate { get; set; }
    public decimal? ApprovalRate { get; set; }
    public int? AvgLatencyMs { get; set; }
}
