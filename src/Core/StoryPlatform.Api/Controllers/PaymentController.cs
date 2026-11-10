using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Payments.DTOs;
using StoryPlatform.Application.Features.Payments.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Mua Gói Đọc, Gói Sáng tạo và Add-on qua SePay (Luồng 2).
/// </summary>
public class PaymentController : BaseApiController
{
    private readonly IPaymentService _paymentService;
    private readonly IPaymentReconciliationService _reconciliationService;

    public PaymentController(IPaymentService paymentService, IPaymentReconciliationService reconciliationService)
    {
        _paymentService = paymentService;
        _reconciliationService = reconciliationService;
    }

    /// <summary>
    /// Danh sách gói đang bán: Gói Đọc, Gói Sáng tạo và Add-on (Add-on bị khoá khi người gọi chưa có gói còn hiệu lực).
    /// </summary>
    [HttpGet("plans")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<List<SubscriptionPlanDto>>>> ListActivePlans(
        CancellationToken cancellationToken)
    {
        var result = await _paymentService.ListActivePlansAsync(GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy danh sách gói Subscription thành công.");
    }

    /// <summary>
    /// Lịch sử giao dịch của chính người gọi, mới nhất trước. Giao dịch Pending còn hạn trả lại mã QR.
    /// </summary>
    [HttpGet("transactions/me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<PagedResult<PaymentTransactionDto>>>> ListMyTransactions(
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await _paymentService.ListMyTransactionsAsync(
            GetCurrentUserId(), Math.Max(pageIndex, 1), Math.Clamp(pageSize, 1, 50), cancellationToken);
        return HandleResult(result, "Lấy lịch sử giao dịch thành công.");
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

    /// <summary>
    /// Administrator xem mọi giao dịch (lọc trạng thái, tìm theo mã hoặc người mua) để xác nhận tay giao dịch đang chờ.
    /// </summary>
    [HttpGet("admin/transactions")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminPaymentTransactionDto>>>> ListAllTransactions(
        [FromQuery] AdminTransactionQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _paymentService.ListAllTransactionsAsync(query, cancellationToken);
        return HandleResult(result, "Lấy danh sách giao dịch thành công.");
    }

    /// <summary>
    /// Administrator xem mọi gói, gồm gói đã tắt bán.
    /// </summary>
    [HttpGet("admin/plans")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<List<AdminSubscriptionPlanDto>>>> ListAllPlans(
        CancellationToken cancellationToken)
    {
        var result = await _paymentService.ListAllPlansAsync(cancellationToken);
        return HandleResult(result, "Lấy danh sách gói thành công.");
    }

    /// <summary>
    /// Administrator sửa tên, giá, số lượt AI và bật hoặc tắt bán một gói (có audit log). Giao dịch đã tạo không đổi.
    /// </summary>
    [HttpPut("admin/plans/{planId:int}")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<AdminSubscriptionPlanDto>>> UpdatePlan(
        int planId, [FromBody] UpdateSubscriptionPlanRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _paymentService.UpdatePlanAsync(GetCurrentUserId(), planId, request, cancellationToken);
        return HandleResult(result, "Cập nhật gói thành công.");
    }

    /// <summary>
    /// Administrator xem giao dịch đã Paid nhưng chưa được áp gói (mặc định các ngoại lệ đang Open).
    /// </summary>
    [HttpGet("admin/reconciliation")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<PagedResult<ReconciliationExceptionDto>>>> ListReconciliationExceptions(
        [FromQuery] string? status, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _reconciliationService.ListAsync(status, pageIndex, pageSize, cancellationToken);
        return HandleResult(result, "Lấy danh sách ngoại lệ đối soát thành công.");
    }

    /// <summary>
    /// Administrator xử lý một ngoại lệ: áp lại gói cho giao dịch (không cộng hai lần) và đóng ngoại lệ.
    /// </summary>
    [HttpPost("admin/reconciliation/{exceptionId:int}/resolve")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<ReconciliationExceptionDto>>> ResolveReconciliationException(
        int exceptionId, CancellationToken cancellationToken)
    {
        var result = await _reconciliationService.ResolveAsync(GetCurrentUserId(), exceptionId, cancellationToken);
        return HandleResult(result, "Đã áp lại gói và đóng ngoại lệ.");
    }
}
