namespace StoryPlatform.Application.Features.BusinessReports.DTOs;

public class GenerateBusinessReportRequestDto
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
}

public class BusinessReportDto
{
    public int Id { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int StoriesGenerated { get; set; }
    public int StoriesApproved { get; set; }
    public int StoriesRejected { get; set; }
    public int ReadingSessionsCompleted { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
}

/// <summary>Báo cáo kỳ dành cho Administrator: thêm số liệu doanh thu đã chụp lúc tạo báo cáo (null với báo cáo cũ).</summary>
public class AdminBusinessReportDto : BusinessReportDto
{
    public RevenueStatisticsDto? Revenue { get; set; }
}

/// <summary>
/// Thống kê doanh thu của một kỳ (F-15). Kỳ tính trọn ngày theo UTC, gồm cả ngày PeriodEnd.
/// Doanh thu chỉ tính giao dịch Paid, theo PaidAt và số tiền lưu trên giao dịch.
/// </summary>
public class RevenueStatisticsDto
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    public long TotalRevenueVnd { get; set; }
    public int PaidTransactionCount { get; set; }
    public TransactionStatusCountsDto TransactionsByStatus { get; set; } = new();

    public List<PlanRevenueDto> RevenueByPlan { get; set; } = [];
    public List<PlanTypeRevenueDto> RevenueByPlanType { get; set; } = [];

    /// <summary>Số phụ huynh có ít nhất một giao dịch Paid trong kỳ.</summary>
    public int BuyingParents { get; set; }
    /// <summary>Trong số đó, người tính đến cuối kỳ đã có từ 2 giao dịch Paid trở lên.</summary>
    public int RepeatBuyingParents { get; set; }

    /// <summary>Giao dịch do Admin xác nhận tay: đã nằm trong doanh thu, đếm riêng để đối chiếu.</summary>
    public int ManuallyMarkedPaidCount { get; set; }
    public long ManuallyMarkedPaidRevenueVnd { get; set; }

    /// <summary>Tổng lượt AI của các lô được cấp trong kỳ.</summary>
    public int CreditsGranted { get; set; }
    public List<CreditSourceCountDto> CreditsGrantedBySource { get; set; } = [];
    /// <summary>Tổng lượt bị trừ trong kỳ, theo lịch sử trừ lượt.</summary>
    public int CreditsUsed { get; set; }

    public decimal ReadingFundRate { get; set; }
    public decimal CreativeFundRate { get; set; }
    /// <summary>Quỹ Creator của kỳ = ReadingFundRate × doanh thu Gói Đọc + CreativeFundRate × doanh thu Gói Sáng tạo, làm tròn xuống.</summary>
    public long CreatorFundVnd { get; set; }
}

/// <summary>Paid đếm theo PaidAt trong kỳ; các trạng thái còn lại đếm theo thời điểm tạo giao dịch.</summary>
public class TransactionStatusCountsDto
{
    public int Paid { get; set; }
    public int Pending { get; set; }
    public int Expired { get; set; }
    public int MismatchAmount { get; set; }
}

public class PlanRevenueDto
{
    public int PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public int TransactionCount { get; set; }
    public long RevenueVnd { get; set; }
}

public class PlanTypeRevenueDto
{
    public string PlanType { get; set; } = string.Empty;
    public int TransactionCount { get; set; }
    public long RevenueVnd { get; set; }
}

public class CreditSourceCountDto
{
    public string Source { get; set; } = string.Empty;
    public int Credits { get; set; }
}
