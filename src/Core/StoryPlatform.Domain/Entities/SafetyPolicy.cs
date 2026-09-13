using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class SafetyPolicy : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int MaxStoryLength { get; set; }
    public ApprovalMode RequiredApprovalMode { get; set; } = ApprovalMode.AlwaysManual;
    public bool ParentalGateEnabled { get; set; } = true;
    public bool ConsentRecorded { get; set; } = false;
    public DateTime? ConsentRecordedAt { get; set; }

    public int ConsentPolicyVersion { get; set; } = 1;
    public decimal? SafetyScoreThreshold { get; set; }
    public decimal? ReadabilityScoreThreshold { get; set; }
    public decimal ComprehensionThresholdPercent { get; set; } = 70m;
    public int ComprehensionWindowSize { get; set; } = 3;
}
