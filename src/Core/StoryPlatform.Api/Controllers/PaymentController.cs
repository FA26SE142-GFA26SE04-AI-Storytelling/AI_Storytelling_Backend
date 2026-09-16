using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Payments.DTOs;
using StoryPlatform.Application.Features.Payments.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Mua gói top-up Token Quota qua SePay (Bước 5.6).
/// </summary>
public class PaymentController : BaseApiController
{
    private readonly IPaymentService _paymentService;

    public PaymentController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    /// <summary>
    /// Danh sách gói Subscription đang active để chọn mua.
    /// </summary>
    [HttpGet("plans")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<List<SubscriptionPlanDto>>>> ListActivePlans(
        CancellationToken cancellationToken)
    {
        var result = await _paymentService.ListActivePlansAsync(cancellationToken);
        return HandleResult(result, "Lấy danh sách gói Subscription thành công.");
    }

    /// <summary>
    /// Tạo giao dịch thanh toán và sinh mã QR VietQR (Bước 5.6.1).
    /// </summary>
    [HttpPost("transactions")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<PaymentTransactionDto>>> CreateTransaction(
        [FromBody] CreatePaymentTransactionRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _paymentService.CreateTransactionAsync(GetCurrentUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<PaymentTransactionDto>.Ok(result, "Tạo giao dịch thanh toán thành công."));
    }

    /// <summary>
    /// Webhook SePay gọi về khi phát hiện giao dịch chuyển khoản (Bước 5.6.2). Xác thực bằng header Authorization.
    /// </summary>
    [HttpPost("webhook/sepay")]
    [AllowAnonymous]
    public async Task<IActionResult> SePayWebhook(
        [FromBody] SePayWebhookPayloadDto payload, CancellationToken cancellationToken)
    {
        await _paymentService.HandleWebhookAsync(Request.Headers.Authorization, payload, cancellationToken);
        return Ok();
    }

    /// <summary>
    /// Danh sách giao dịch mismatch_amount cần Administrator xử lý thủ công.
    /// </summary>
    [HttpGet("mismatched")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<List<PaymentTransactionDto>>>> ListMismatched(
        CancellationToken cancellationToken)
    {
        var result = await _paymentService.ListMismatchedAsync(cancellationToken);
        return HandleResult(result, "Lấy danh sách giao dịch mismatch_amount thành công.");
    }

    /// <summary>
    /// Administrator xác nhận thủ công một giao dịch đã nhận được tiền (có audit log).
    /// </summary>
    [HttpPost("transactions/{transactionId:int}/mark-paid")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<PaymentTransactionDto>>> MarkPaidManually(
        int transactionId, CancellationToken cancellationToken)
    {
        var result = await _paymentService.MarkPaidManuallyAsync(GetCurrentUserId(), transactionId, cancellationToken);
        return HandleResult(result, "Xác nhận thanh toán thủ công thành công.");
    }
}
