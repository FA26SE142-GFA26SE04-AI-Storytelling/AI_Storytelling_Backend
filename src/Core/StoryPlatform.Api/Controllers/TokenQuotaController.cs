using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.TokenQuota.DTOs;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Tra cứu lượt tạo truyện AI (Luồng 2, 2d).
/// </summary>
public class TokenQuotaController : BaseApiController
{
    private readonly ITokenQuotaService _tokenQuotaService;

    public TokenQuotaController(ITokenQuotaService tokenQuotaService)
    {
        _tokenQuotaService = tokenQuotaService;
    }

    /// <summary>
    /// Lượt AI còn lại của tài khoản phụ huynh: lượt dùng thử, lượt chu kỳ, lượt Add-on và gói đang hiệu lực.
    /// </summary>
    [HttpGet("me")]
    [Authorize(Roles = "User")]
    public async Task<ActionResult<ApiResponse<MyQuotaDto>>> GetMyQuota(CancellationToken cancellationToken)
    {
        var result = await _tokenQuotaService.GetMyQuotaAsync(GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy thông tin lượt tạo truyện thành công.");
    }

    /// <summary>
    /// Xem lượt AI còn lại áp dụng cho 1 trẻ — lượt tính theo tài khoản chủ hồ sơ (Owner/Administrator).
    /// </summary>
    [HttpGet("child/{childProfileId:int}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<TokenQuotaStatusDto>>> GetChildStatus(
        int childProfileId, CancellationToken cancellationToken)
    {
        var result = await _tokenQuotaService.GetStatusForChildAsync(GetCurrentUserId(), childProfileId, cancellationToken);
        return HandleResult(result, "Lấy thông tin Token Quota thành công.");
    }

    /// <summary>
    /// Administrator cấp (amount dương) hoặc trừ (amount âm) lượt AI cho một tài khoản phụ huynh; bắt buộc ghi lý do.
    /// </summary>
    [HttpPost("admin/adjustments")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<CreditAdjustmentDto>>> Adjust(
        [FromBody] AdjustCreditsRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _tokenQuotaService.AdjustAsync(GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Điều chỉnh lượt thành công.");
    }

    /// <summary>
    /// Lịch sử điều chỉnh lượt, mới nhất trước; lọc theo tài khoản nếu có userId.
    /// </summary>
    [HttpGet("admin/adjustments")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<PagedResult<CreditAdjustmentDto>>>> ListAdjustments(
        [FromQuery] int? userId, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _tokenQuotaService.ListAdjustmentsAsync(userId, pageIndex, pageSize, cancellationToken);
        return HandleResult(result, "Lấy lịch sử điều chỉnh lượt thành công.");
    }

    /// <summary>
    /// Administrator xem gói đang hiệu lực và lượt AI còn lại của một tài khoản.
    /// </summary>
    [HttpGet("admin/users/{userId:int}")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<MyQuotaDto>>> GetUserQuota(int userId, CancellationToken cancellationToken)
    {
        var result = await _tokenQuotaService.GetQuotaForUserAsync(userId, cancellationToken);
        return HandleResult(result, "Lấy thông tin lượt của tài khoản thành công.");
    }
}
