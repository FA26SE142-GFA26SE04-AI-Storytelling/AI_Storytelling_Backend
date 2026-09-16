using StoryPlatform.Application.Features.Payments.DTOs;

namespace StoryPlatform.Application.Features.Payments.Interfaces;

/// <summary>
/// Mua gói top-up Token Quota qua SePay (Bước 5.6).
/// </summary>
public interface IPaymentService
{
    Task<List<SubscriptionPlanDto>> ListActivePlansAsync(CancellationToken cancellationToken = default);

    Task<PaymentTransactionDto> CreateTransactionAsync(
        int payerUserId, CreatePaymentTransactionRequestDto request, CancellationToken cancellationToken = default);

    Task HandleWebhookAsync(
        string? authorizationHeaderValue, SePayWebhookPayloadDto payload, CancellationToken cancellationToken = default);

    Task<List<PaymentTransactionDto>> ListMismatchedAsync(CancellationToken cancellationToken = default);

    Task<PaymentTransactionDto> MarkPaidManuallyAsync(
        int adminUserId, int transactionId, CancellationToken cancellationToken = default);
}
