namespace StoryPlatform.Application.Features.TokenQuota.DTOs;

public class TokenQuotaStatusDto
{
    public bool IsUnlimited { get; set; }
    public string? Scope { get; set; }
    public int? QuotaLimit { get; set; }
    public int? QuotaUsed { get; set; }
    public int? Remaining { get; set; }
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
}

public class QuotaPlanDto
{
    public string PlanType { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
}

/// <summary>
/// Lượt AI còn lại của tài khoản phụ huynh, tách theo nguồn (UC-L2-12). Plan null = mức Miễn phí.
/// </summary>
public class MyQuotaDto
{
    public QuotaPlanDto? Plan { get; set; }
    public QuotaPlanDto? NextPlan { get; set; }
    public int TrialCredits { get; set; }
    public int CycleCredits { get; set; }
    public int AddOnCredits { get; set; }
    public int OtherCredits { get; set; }
    public int TotalRemaining { get; set; }
}

/// <summary>Admin cấp (Amount dương) hoặc trừ (Amount âm) lượt cho một tài khoản phụ huynh; Reason bắt buộc.</summary>
public class AdjustCreditsRequestDto
{
    public int UserId { get; set; }
    public int Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class CreditAdjustmentDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int AdminId { get; set; }
    public string AdminName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    /// <summary>Tổng lượt còn lại ngay sau lần điều chỉnh; chỉ có ở response của thao tác điều chỉnh.</summary>
    public int? TotalRemainingAfter { get; set; }
}
