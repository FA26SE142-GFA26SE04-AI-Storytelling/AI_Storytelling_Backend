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
    /// <summary>Số dư tối thiểu để Creator được chi (VND); 0 = không có mức tối thiểu.</summary>
    public long MinimumPayoutVnd { get; set; }
    /// <summary>Ngày trong tháng (1-28, giờ Việt Nam) chốt kỳ thu nhập Creator của tháng trước.</summary>
    public int CreatorPeriodCloseDay { get; set; }
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
    /// <summary>Số lần sinh lại dàn ý miễn phí trong một truyện, mặc định 3.</summary>
    public int OutlineRegenerationLimit { get; set; }
    /// <summary>Số lần sinh lại nội dung, mặc định 2 — chỉ lưu, chưa có chức năng sinh lại toàn bộ nội dung.</summary>
    public int ContentRegenerationLimit { get; set; }
    /// <summary>Số ảnh được sinh lại trong một truyện, mặc định 3.</summary>
    public int ImageRegenerationLimit { get; set; }
    /// <summary>Số lần sinh lại audio trong một truyện, mặc định 2.</summary>
    public int AudioRegenerationLimit { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Admin cập nhật toàn bộ cấu hình chung.</summary>
public class UpdatePlanSettingsRequestDto
{
    public int ReadingFundPercent { get; set; }
    public int CreativeFundPercent { get; set; }
    /// <summary>Bỏ trống thì giữ mức hiện tại (client cũ chưa gửi trường này). 0 = không có mức tối thiểu.</summary>
    public long? MinimumPayoutVnd { get; set; }
    /// <summary>Bỏ trống thì giữ ngày chốt hiện tại (client cũ chưa gửi trường này).</summary>
    public int? CreatorPeriodCloseDay { get; set; }
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
    public int OutlineRegenerationLimit { get; set; } = 3;
    public int ContentRegenerationLimit { get; set; } = 2;
    public int ImageRegenerationLimit { get; set; } = 3;
    public int AudioRegenerationLimit { get; set; } = 2;
}

/// <summary>Số lần đã sinh lại / giới hạn của một loại trong truyện (Luồng 2, 3).</summary>
public sealed record RegenerationCounterDto
{
    public int Used { get; set; }
    public int Limit { get; set; }
    public int Remaining { get; set; }

    public static RegenerationCounterDto From(StoryPlatform.Application.Common.RegenerationCounter counter) =>
        new() { Used = counter.Used, Limit = counter.Limit, Remaining = counter.Remaining };
}
