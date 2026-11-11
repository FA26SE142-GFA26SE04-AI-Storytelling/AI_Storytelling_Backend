namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Cấu hình chung của gói và hạn mức (Luồng 5, 5a), một dòng duy nhất Id = 1.
/// Giá trị khởi tạo là cấu hình mặc định: <c>new PlanSettings()</c> dùng khi DB chưa có dòng.
/// </summary>
public class PlanSettings : BaseEntity
{
    public const int SingletonId = 1;

    public int ReadingFundPercent { get; set; } = 30;
    public int CreativeFundPercent { get; set; } = 10;

    public int CreatorMonthlyAiCredits { get; set; } = 2;
    public int CreatorMonthlySubmitCredits { get; set; } = 5;
    /// <summary>Hạn mức Creator đối tác; chỉ lưu, chưa có hạng đối tác.</summary>
    public int PartnerMonthlyAiCredits { get; set; } = 6;
    public int PartnerMonthlySubmitCredits { get; set; } = 10;

    public int MinimumReadSeconds { get; set; } = 60;
    public int TrialAiCredits { get; set; } = 1;

    public int FreeMaxChildProfiles { get; set; } = 1;
    /// <summary>Số truyện thư viện công khai mới mỗi tuần; null = không giới hạn.</summary>
    public int? FreeWeeklyNewLibraryStories { get; set; } = 3;
    public bool FreeCanCopyRoadmapTemplates { get; set; }
    /// <summary>Chỉ lưu, chưa chặn (để sau Defense 1.1).</summary>
    public int? FreeReadAloudPerMonth { get; set; } = 5;
    public int FreeWritingAssistPerDay { get; set; } = 5;

    public int CreatorWritingAssistPerDay { get; set; } = 20;
    public int PartnerWritingAssistPerDay { get; set; } = 40;
}
