using System.Text.Json.Serialization;

namespace StoryPlatform.Application.Features.Payments.DTOs;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ApplicableScope { get; set; } = string.Empty;
    public int PriceVnd { get; set; }
    public int QuotaAmount { get; set; }
}

public class CreatePaymentTransactionRequestDto
{
    public int PlanId { get; set; }
    public int? OrganizationId { get; set; }
}

public class PaymentTransactionDto
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public int Amount { get; set; }
    public string TransactionCode { get; set; } = string.Empty;
    public string? QrCodeUrl { get; set; }
    public string Status { get; set; } = string.Empty;
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
