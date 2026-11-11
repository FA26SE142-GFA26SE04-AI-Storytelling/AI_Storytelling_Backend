using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class SubscriptionPlan : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public PlanType PlanType { get; set; } = PlanType.Legacy;
    public PlanAudience Audience { get; set; } = PlanAudience.User;
    public int PriceVnd { get; set; }
    public int AiCredits { get; set; }
    /// <summary>Số lượt gửi duyệt của gói (chỉ Gói Creator).</summary>
    public int SubmitCredits { get; set; }
    public bool IsActive { get; set; } = true;

    // Quyền lợi (chỉ có nghĩa với gói chu kỳ Reading/Creative).
    public int MaxChildProfiles { get; set; }
    /// <summary>Số truyện thư viện công khai mới mỗi tuần; null = không giới hạn.</summary>
    public int? WeeklyNewLibraryStories { get; set; }
    public bool CanCopyRoadmapTemplates { get; set; }
    /// <summary>Chỉ lưu, chưa chặn (để sau Defense 1.1).</summary>
    public int? ReadAloudPerMonth { get; set; }
    public int WritingAssistPerDay { get; set; }
}
