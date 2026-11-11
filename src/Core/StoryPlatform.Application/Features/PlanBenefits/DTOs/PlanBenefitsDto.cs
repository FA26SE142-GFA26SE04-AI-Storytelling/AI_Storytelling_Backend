namespace StoryPlatform.Application.Features.PlanBenefits.DTOs;

/// <summary>Quyền lợi đang áp dụng cho một tài khoản (gói chu kỳ đang hiệu lực, mức Miễn phí hoặc Creator).</summary>
public class PlanBenefitsDto
{
    public const string FreeTier = "Free";
    public const string CreatorTier = "Creator";

    /// <summary>"Free", "Creator" hoặc tên gói đang dùng.</summary>
    public string Tier { get; set; } = FreeTier;
    public int? PlanId { get; set; }
    public int MaxChildProfiles { get; set; }
    /// <summary>null = không giới hạn.</summary>
    public int? WeeklyNewLibraryStories { get; set; }
    public bool CanCopyRoadmapTemplates { get; set; }
    /// <summary>Chỉ hiển thị, chưa chặn.</summary>
    public int? ReadAloudPerMonth { get; set; }
    public int WritingAssistPerDay { get; set; }
}
