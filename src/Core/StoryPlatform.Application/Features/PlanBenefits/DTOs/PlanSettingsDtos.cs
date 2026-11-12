namespace StoryPlatform.Application.Features.PlanBenefits.DTOs;

/// <summary>Cấu hình chung của gói và hạn mức (Luồng 5, 5a).</summary>
public class PlanSettingsDto
{
    /// <summary>Tỷ lệ quỹ Creator trên doanh thu Gói Đọc (%), mặc định 30.</summary>
    public int ReadingFundPercent { get; set; }
    /// <summary>Tỷ lệ quỹ Creator trên doanh thu Gói Sáng tạo (%), mặc định 10.</summary>
    public int CreativeFundPercent { get; set; }
    public int CreatorMonthlyAiCredits { get; set; }
    public int CreatorMonthlySubmitCredits { get; set; }
    /// <summary>Hạn mức Creator đối tác — chỉ lưu, chưa có hạng đối tác.</summary>
    public int PartnerMonthlyAiCredits { get; set; }
    public int PartnerMonthlySubmitCredits { get; set; }
    /// <summary>Thời gian đọc tối thiểu của lượt đọc hợp lệ (giây), 1–3600.</summary>
    public int MinimumReadSeconds { get; set; }
    /// <summary>Lượt AI dùng thử cấp một lần khi có hồ sơ trẻ Active đầu tiên.</summary>
    public int TrialAiCredits { get; set; }
    public int FreeMaxChildProfiles { get; set; }
    /// <summary>null = không giới hạn.</summary>
    public int? FreeWeeklyNewLibraryStories { get; set; }
    public bool FreeCanCopyRoadmapTemplates { get; set; }
    /// <summary>Chỉ lưu, chưa chặn.</summary>
    public int? FreeReadAloudPerMonth { get; set; }
    public int FreeWritingAssistPerDay { get; set; }
    public int CreatorWritingAssistPerDay { get; set; }
    public int PartnerWritingAssistPerDay { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Admin cập nhật toàn bộ cấu hình chung.</summary>
public class UpdatePlanSettingsRequestDto
{
    public int ReadingFundPercent { get; set; }
    public int CreativeFundPercent { get; set; }
    public int CreatorMonthlyAiCredits { get; set; }
    public int CreatorMonthlySubmitCredits { get; set; }
    public int PartnerMonthlyAiCredits { get; set; }
    public int PartnerMonthlySubmitCredits { get; set; }
    public int MinimumReadSeconds { get; set; }
    public int TrialAiCredits { get; set; }
    public int FreeMaxChildProfiles { get; set; }
    public int? FreeWeeklyNewLibraryStories { get; set; }
    public bool FreeCanCopyRoadmapTemplates { get; set; }
    public int? FreeReadAloudPerMonth { get; set; }
    public int FreeWritingAssistPerDay { get; set; }
    public int CreatorWritingAssistPerDay { get; set; }
    public int PartnerWritingAssistPerDay { get; set; }
}
