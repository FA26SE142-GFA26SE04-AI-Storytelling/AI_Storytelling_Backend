using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.CreatorPayouts.DTOs;
using StoryPlatform.Application.Features.CreatorPayouts.Interfaces;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Features.AccessControl;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Chi trả cho Creator (Luồng 5, 5h). Hệ thống không chuyển tiền: Admin chuyển khoản ngoài hệ thống rồi đánh dấu Đã chi.
/// Creator chỉ xem và sửa dữ liệu của chính mình (mã Creator lấy từ token); việc cộng thu nhập kỳ vào số dư chạy tự động
/// vào ngày chốt Admin đặt trong cấu hình gói, mặc định ngày 8 hằng tháng (SubscriptionCycleWorker), hoặc khi Admin bấm Chốt kỳ ngay.
/// </summary>
[Route("api/v1/creator-payouts")]
public class CreatorPayoutController : BaseApiController
{
    private readonly ICreatorPayoutService _service;

    public CreatorPayoutController(ICreatorPayoutService service)
    {
        _service = service;
    }

    /// <summary>Số dư, thông tin nhận tiền (hoặc lời nhắc nhập), lịch sử nhận tiền và thu nhập các kỳ đã chốt.</summary>
    [HttpGet("me")]
    [HasPermission(Permissions.Creator.PayoutViewOwn)]
    public async Task<ActionResult<ApiResponse<CreatorPayoutOverviewDto>>> GetMine(CancellationToken cancellationToken)
    {
        var result = await _service.GetOverviewAsync(GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy số dư và lịch sử nhận tiền thành công.");
    }

    /// <summary>Nhập hoặc sửa thông tin nhận tiền: ngân hàng, số tài khoản, tên chủ tài khoản.</summary>
    [HttpPut("me/account")]
    [HasPermission(Permissions.Creator.PayoutViewOwn)]
    public async Task<ActionResult<ApiResponse<CreatorPayoutAccountDto>>> UpdateMyAccount(
        [FromBody] UpdateCreatorPayoutAccountRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdatePayoutAccountAsync(GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Đã lưu thông tin nhận tiền.");
    }

    /// <summary>Danh sách chi: Creator Active, số dư đạt mức chi tối thiểu Admin đặt, đã có thông tin nhận tiền.</summary>
    [HttpGet("/api/v1/admin/creator-payouts/due")]
    [HasPermission(Permissions.Admin.CreatorPayoutManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CreatorPayoutDueDto>>>> GetDue(CancellationToken cancellationToken)
    {
        var result = await _service.GetDueAsync(cancellationToken);
        return HandleResult(result, "Lấy danh sách chi thành công.");
    }

    /// <summary>Số dư, thông tin nhận tiền và lịch sử của một Creator.</summary>
    [HttpGet("/api/v1/admin/creator-payouts/creators/{creatorUserId:int}")]
    [HasPermission(Permissions.Admin.CreatorPayoutManage)]
    public async Task<ActionResult<ApiResponse<CreatorPayoutOverviewDto>>> GetCreator(
        int creatorUserId, CancellationToken cancellationToken)
    {
        var result = await _service.GetOverviewAsync(creatorUserId, cancellationToken);
        return HandleResult(result, "Lấy số dư và lịch sử chi của nhà sáng tạo thành công.");
    }

    /// <summary>Đánh dấu Đã chi đúng bằng số dư, kèm mã giao dịch ngân hàng và ngày chuyển; số dư về 0.</summary>
    [HttpPost("/api/v1/admin/creator-payouts/creators/{creatorUserId:int}/mark-paid")]
    [HasPermission(Permissions.Admin.CreatorPayoutManage)]
    public async Task<ActionResult<ApiResponse<CreatorPayoutDto>>> MarkPaid(
        int creatorUserId, [FromBody] MarkCreatorPayoutPaidRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _service.MarkPaidAsync(GetCurrentUserId(), creatorUserId, request, cancellationToken);
        return HandleResult(result, "Đã ghi nhận lần chi.");
    }

    /// <summary>
    /// Chốt kỳ ngay (dùng khi demo): chốt thu nhập từ ngày sau kỳ đã chốt gần nhất đến hết hôm nay và cộng vào số dư ngay,
    /// không chờ ngày chốt hằng tháng. Lượt đọc còn lại của hôm nay tính vào lần chốt sau từ ngày mai.
    /// </summary>
    [HttpPost("/api/v1/admin/creator-payouts/periods/close-now")]
    [HasPermission(Permissions.Admin.CreatorPayoutManage)]
    public async Task<ActionResult<ApiResponse<CreatorEarningPeriodDto>>> CloseNow(CancellationToken cancellationToken)
    {
        var result = await _service.CloseNowAsync(GetCurrentUserId(), DateTime.UtcNow, cancellationToken);
        return HandleResult(result, "Đã chốt kỳ thu nhập đến hết hôm nay.");
    }

    /// <summary>Huỷ một lần chi (chuyển nhầm, ngân hàng trả lại) kèm lý do; số dư được cộng lại.</summary>
    [HttpPost("/api/v1/admin/creator-payouts/{payoutId:int}/cancel")]
    [HasPermission(Permissions.Admin.CreatorPayoutManage)]
    public async Task<ActionResult<ApiResponse<CreatorPayoutDto>>> Cancel(
        int payoutId, [FromBody] CancelCreatorPayoutRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _service.CancelPayoutAsync(GetCurrentUserId(), payoutId, request, cancellationToken);
        return HandleResult(result, "Đã huỷ lần chi.");
    }
}
