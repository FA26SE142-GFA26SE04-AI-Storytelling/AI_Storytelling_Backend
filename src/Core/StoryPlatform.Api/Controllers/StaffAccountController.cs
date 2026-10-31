using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Administration.DTOs;
using StoryPlatform.Application.Features.Administration.Interfaces;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Administrator quản lý tài khoản vận hành (Moderator và Administrator).
/// Vai trò chọn bằng "role" khi mời và khi lọc danh sách; các thao tác theo userId tự đọc vai trò.
/// </summary>
[Authorize(Roles = "Administrator")]
public class StaffAccountController : BaseApiController
{
    private readonly IStaffAccountService _staffAccountService;

    public StaffAccountController(IStaffAccountService staffAccountService)
    {
        _staffAccountService = staffAccountService;
    }

    /// <summary>Danh sách tài khoản vận hành; role = Moderator hoặc Administrator để lọc, bỏ trống để lấy cả hai.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<StaffAccountDto>>>> List(
        [FromQuery] UserRole? role, CancellationToken cancellationToken)
    {
        var result = await _staffAccountService.ListAsync(role, cancellationToken);
        return HandleResult(result, "Lấy danh sách tài khoản vận hành thành công.");
    }

    /// <summary>Mời Moderator hoặc Administrator mới qua email; mã mời có hiệu lực 72 giờ.</summary>
    [HttpPost("invite")]
    public async Task<ActionResult<ApiResponse<StaffAccountDto>>> Invite(
        [FromBody] InviteStaffRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _staffAccountService.InviteAsync(
            GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, $"Đã gửi lời mời {result.Role} tới email.");
    }

    /// <summary>Cấp mã mời mới cho tài khoản chưa kích hoạt.</summary>
    [HttpPost("{userId:int}/resend-invite")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<ApiResponse<StaffAccountDto>>> ResendInvite(
        int userId, CancellationToken cancellationToken)
    {
        var result = await _staffAccountService.ResendInviteAsync(
            GetCurrentUserId(), userId, cancellationToken);
        return HandleResult(result, "Đã gửi lại lời mời.");
    }

    /// <summary>Hủy lời mời chưa dùng: xóa tài khoản chưa kích hoạt để mời lại được cùng email.</summary>
    [HttpPost("{userId:int}/cancel-invite")]
    public async Task<ActionResult<ApiResponse<StaffAccountDto>>> CancelInvite(
        int userId, CancellationToken cancellationToken)
    {
        var result = await _staffAccountService.CancelInviteAsync(
            GetCurrentUserId(), userId, cancellationToken);
        return HandleResult(result, "Đã hủy lời mời.");
    }

    /// <summary>Vô hiệu hóa tài khoản và đăng xuất mọi phiên ngay.</summary>
    [HttpPost("{userId:int}/deactivate")]
    public async Task<ActionResult<ApiResponse<StaffAccountDto>>> Deactivate(
        int userId, [FromBody] ReasonRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _staffAccountService.DeactivateAsync(
            GetCurrentUserId(), userId, request.Reason, cancellationToken);
        return HandleResult(result, $"Đã vô hiệu hóa tài khoản {result.Role}.");
    }

    /// <summary>Kích hoạt lại tài khoản đã bị vô hiệu hóa.</summary>
    [HttpPost("{userId:int}/reactivate")]
    public async Task<ActionResult<ApiResponse<StaffAccountDto>>> Reactivate(
        int userId, CancellationToken cancellationToken)
    {
        var result = await _staffAccountService.ReactivateAsync(
            GetCurrentUserId(), userId, cancellationToken);
        return HandleResult(result, $"Đã kích hoạt lại tài khoản {result.Role}.");
    }
}
