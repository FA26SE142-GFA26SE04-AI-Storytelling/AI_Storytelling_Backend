using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Payments.DTOs;

namespace StoryPlatform.Application.Features.Payments.Interfaces;

/// <summary>
/// Mua Gói Đọc, Gói Sáng tạo và Add-on qua SePay (Luồng 2).
/// </summary>
public interface IPaymentService
{
    Task<List<SubscriptionPlanDto>> ListActivePlansAsync(int userId, CancellationToken cancellationToken = default);

    Task<PaymentTransactionDto> CreateTransactionAsync(
        int payerUserId, CreatePaymentTransactionRequestDto request, CancellationToken cancellationToken = default);

    Task HandleWebhookAsync(
        string? authorizationHeaderValue, SePayWebhookPayloadDto payload, CancellationToken cancellationToken = default);

    Task<List<PaymentTransactionDto>> ListMismatchedAsync(CancellationToken cancellationToken = default);

    /// <summary>Giao dịch của chính người gọi, mới nhất trước.</summary>
    Task<PagedResult<PaymentTransactionDto>> ListMyTransactionsAsync(
        int userId, int pageIndex, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Mọi giao dịch, mới nhất trước, để Admin tìm và xác nhận tay giao dịch đang chờ.</summary>
    Task<PagedResult<AdminPaymentTransactionDto>> ListAllTransactionsAsync(
        AdminTransactionQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>Mọi gói, gồm gói tắt bán và gói Legacy.</summary>
    Task<List<AdminSubscriptionPlanDto>> ListAllPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Sửa tên, giá, số lượt và trạng thái bán; chỉ áp dụng cho giao dịch tạo sau đó.</summary>
    Task<AdminSubscriptionPlanDto> UpdatePlanAsync(
        int adminUserId, int planId, UpdateSubscriptionPlanRequestDto request, CancellationToken cancellationToken = default);

    Task<PaymentTransactionDto> MarkPaidManuallyAsync(
        int adminUserId, int transactionId, CancellationToken cancellationToken = default);
}
