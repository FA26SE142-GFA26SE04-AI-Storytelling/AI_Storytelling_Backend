using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Administration.DTOs;
using StoryPlatform.Application.Features.Administration.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Administrator quản lý tài khoản Moderator.</summary>
[Authorize(Roles = "Administrator")]
public class ModeratorAccountController : BaseApiController
{
    private readonly IModeratorAccountService _moderatorAccountService;

    public ModeratorAccountController(IModeratorAccountService moderatorAccountService)
    {
        _moderatorAccountService = moderatorAccountService;
    }

    /// <summary>Danh sách Moderator và trạng thái từng người.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ModeratorAccountDto>>>> List(
        CancellationToken cancellationToken)
    {
        var result = await _moderatorAccountService.ListAsync(cancellationToken);
        return HandleResult(result, "Lấy danh sách Moderator thành công.");
    }

    /// <summary>Mời Moderator mới qua email; mã mời có hiệu lực 72 giờ.</summary>
    [HttpPost("invite")]
    public async Task<ActionResult<ApiResponse<ModeratorAccountDto>>> Invite(
        [FromBody] InviteModeratorRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _moderatorAccountService.InviteAsync(
            GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Đã gửi lời mời tới email của Moderator.");
    }

    /// <summary>Cấp mã mời mới cho Moderator chưa kích hoạt.</summary>
    [HttpPost("{userId:int}/resend-invite")]
    public async Task<ActionResult<ApiResponse<ModeratorAccountDto>>> ResendInvite(
        int userId, CancellationToken cancellationToken)
    {
        var result = await _moderatorAccountService.ResendInviteAsync(
            GetCurrentUserId(), userId, cancellationToken);
        return HandleResult(result, "Đã gửi lại lời mời.");
    }

    /// <summary>Vô hiệu hóa Moderator và đăng xuất mọi phiên ngay.</summary>
    [HttpPost("{userId:int}/deactivate")]
    public async Task<ActionResult<ApiResponse<ModeratorAccountDto>>> Deactivate(
        int userId, [FromBody] ReasonRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _moderatorAccountService.DeactivateAsync(
            GetCurrentUserId(), userId, request.Reason, cancellationToken);
        return HandleResult(result, "Đã vô hiệu hóa tài khoản Moderator.");
    }

    /// <summary>Kích hoạt lại Moderator đã bị vô hiệu hóa.</summary>
    [HttpPost("{userId:int}/reactivate")]
    public async Task<ActionResult<ApiResponse<ModeratorAccountDto>>> Reactivate(
        int userId, CancellationToken cancellationToken)
    {
        var result = await _moderatorAccountService.ReactivateAsync(
            GetCurrentUserId(), userId, cancellationToken);
        return HandleResult(result, "Đã kích hoạt lại tài khoản Moderator.");
    }
}
