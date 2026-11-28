namespace StoryPlatform.Application.Features.PlanBenefits.DTOs;

/// <summary>Quyền lợi đang áp dụng cho một tài khoản (gói chu kỳ đang hiệu lực, mức Miễn phí hoặc Creator).</summary>
public class PlanBenefitsDto
{
    public const string WeekPeriod = "Week";
    public const string CyclePeriod = "Cycle";
    public const string FreeTier = "Free";
    public const string CreatorTier = "Creator";
    /// <summary>Creator đối tác (Luồng 5, 5g).</summary>
    public const string PartnerCreatorTier = "PartnerCreator";

    /// <summary>"Free", "Creator", "PartnerCreator" hoặc tên gói đang dùng.</summary>
    public string Tier { get; set; } = FreeTier;
    public int? PlanId { get; set; }
    public int MaxChildProfiles { get; set; }
    /// <summary>Hạn mức truyện thư viện mới của kỳ hiện tại (tuần với Miễn phí/Creator, chu kỳ với gói trả phí); null là không giới hạn.</summary>
    public int? WeeklyNewLibraryStories { get; set; }
    public string NewLibraryStoriesPeriod { get; set; } = WeekPeriod;
    /// <summary>Thời điểm bắt đầu kỳ theo UTC.</summary>
    public DateTime NewLibraryStoriesPeriodStart { get; set; }
    /// <summary>Mốc kết thúc không bao gồm trong kỳ, theo UTC.</summary>
    public DateTime NewLibraryStoriesResetsAt { get; set; }
    public bool CanCopyRoadmapTemplates { get; set; }
    /// <summary>Chỉ hiển thị, chưa chặn.</summary>
    public int? ReadAloudPerMonth { get; set; }
    public int WritingAssistPerDay { get; set; }
}
