using System.Text.Json.Serialization;

namespace StoryPlatform.Application.Features.Payments.DTOs;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Reading | Creative | AddOn | CreatorPack.</summary>
    public string PlanType { get; set; } = string.Empty;
    /// <summary>User (phụ huynh) | Creator.</summary>
    public string Audience { get; set; } = string.Empty;
    public int PriceVnd { get; set; }
    public int AiCredits { get; set; }
    /// <summary>Số lượt gửi duyệt (chỉ Gói Creator).</summary>
    public int SubmitCredits { get; set; }
    /// <summary>False khi người gọi chưa đủ điều kiện mua (Add-on cần đang có gói còn hiệu lực).</summary>
    public bool IsPurchasable { get; set; } = true;
    public string? LockedReason { get; set; }
    /// <summary>Quyền lợi của gói chu kỳ (Gói Đọc, Gói Sáng tạo); null với Add-on và Gói Creator.</summary>
    public PlanBenefitsInputDto? Benefits { get; set; }
}

/// <summary>Quyền lợi của gói chu kỳ (Luồng 2, 2h).</summary>
public class PlanBenefitsInputDto
{
    /// <summary>Số hồ sơ trẻ tối đa (≥ 1).</summary>
    public int MaxChildProfiles { get; set; }
    /// <summary>Số truyện thư viện công khai mới mỗi tuần; null = không giới hạn.</summary>
    public int? WeeklyNewLibraryStories { get; set; }
    public bool CanCopyRoadmapTemplates { get; set; }
    /// <summary>Số lần đọc to mỗi tháng — chỉ lưu, chưa chặn.</summary>
    public int? ReadAloudPerMonth { get; set; }
    /// <summary>Số lần dùng trợ lý viết mỗi ngày (giờ Việt Nam).</summary>
    public int WritingAssistPerDay { get; set; }
}

public class CreatePaymentTransactionRequestDto
{
    public int PlanId { get; set; }
}

public class PaymentTransactionDto
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public int Amount { get; set; }
    public string TransactionCode { get; set; } = string.Empty;
    /// <summary>Chỉ có khi giao dịch Pending còn hạn.</summary>
    public string? QrCodeUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? SepayTransactionId { get; set; }
}

/// <summary>
/// Payload webhook SePay gửi tới — chỉ ánh xạ các trường cần dùng, tên trùng JSON key thật của SePay.
/// </summary>
public class SePayWebhookPayloadDto
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("transferAmount")]
    public int TransferAmount { get; set; }

    [JsonPropertyName("referenceCode")]
    public string? ReferenceCode { get; set; }
}

/// <summary>Bộ lọc danh sách giao dịch của Admin. Search khớp một phần mã giao dịch, email hoặc tên đăng nhập người mua.</summary>
public class AdminTransactionQueryDto
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Status { get; set; }
    public string? Search { get; set; }
}

/// <summary>Giao dịch kèm người mua, cho Admin. Status và QrCodeUrl là giá trị đang lưu.</summary>
public class AdminPaymentTransactionDto : PaymentTransactionDto
{
    public int PayerUserId { get; set; }
    public string PayerEmail { get; set; } = string.Empty;
    public string PayerName { get; set; } = string.Empty;
}

public class AdminSubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int PriceVnd { get; set; }
    public int AiCredits { get; set; }
    public int SubmitCredits { get; set; }
    public bool IsActive { get; set; }
    public PlanBenefitsInputDto? Benefits { get; set; }
}

/// <summary>Admin tạo gói mới (Luồng 5, 5a). Gói Reading/Creative bắt buộc có quyền lợi.</summary>
public class CreateSubscriptionPlanRequestDto
{
    public string Name { get; set; } = string.Empty;
    /// <summary>User (phụ huynh) | Creator.</summary>
    public string Audience { get; set; } = string.Empty;
    /// <summary>Reading | Creative | AddOn (phụ huynh) hoặc CreatorPack (Creator).</summary>
    public string PlanType { get; set; } = string.Empty;
    public int PriceVnd { get; set; }
    public int AiCredits { get; set; }
    /// <summary>Số lượt gửi duyệt — chỉ dùng cho Gói Creator.</summary>
    public int SubmitCredits { get; set; }
    public bool IsActive { get; set; } = true;
    public PlanBenefitsInputDto? Benefits { get; set; }
}

public class UpdateSubscriptionPlanRequestDto
{
    public string Name { get; set; } = string.Empty;
    public int PriceVnd { get; set; }
    public int AiCredits { get; set; }
    /// <summary>Số lượt gửi duyệt (Gói Creator); bỏ trống thì giữ nguyên.</summary>
    public int? SubmitCredits { get; set; }
    public bool IsActive { get; set; }
    /// <summary>Quyền lợi (chỉ gói Reading/Creative); bỏ trống thì giữ nguyên.</summary>
    public PlanBenefitsInputDto? Benefits { get; set; }
}

/// <summary>Giao dịch đã Paid nhưng chưa được áp gói, chờ Admin xử lý (UC-L2-13).</summary>
public class ReconciliationExceptionDto
{
    public int Id { get; set; }
    /// <summary>Open | Resolved.</summary>
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedByName { get; set; }
    public AdminPaymentTransactionDto Transaction { get; set; } = new();
}
