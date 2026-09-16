using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Administration.DTOs;
using StoryPlatform.Application.Features.Administration.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Cấp/thu hồi quyền Administrator (Bước 5.1b).
/// </summary>
[Authorize(Roles = "Administrator")]
public class AdminAccountController : BaseApiController
{
    private readonly IAdminAccountService _adminAccountService;

    public AdminAccountController(IAdminAccountService adminAccountService)
    {
        _adminAccountService = adminAccountService;
    }

    /// <summary>
    /// Cấp quyền Administrator cho một tài khoản đích theo email.
    /// </summary>
    [HttpPost("grant")]
    public async Task<ActionResult<ApiResponse<AdministratorAccountDto>>> GrantAdministrator(
        [FromBody] GrantAdministratorRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _adminAccountService.GrantAsync(GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Cấp quyền Administrator thành công.");
    }

    /// <summary>
    /// Thu hồi quyền Administrator của một tài khoản đích, khôi phục về vai trò trước đó.
    /// </summary>
    [HttpPost("{targetUserId:int}/revoke")]
    public async Task<ActionResult<ApiResponse<AdministratorAccountDto>>> RevokeAdministrator(
        int targetUserId, CancellationToken cancellationToken)
    {
        var result = await _adminAccountService.RevokeAsync(GetCurrentUserId(), targetUserId, cancellationToken);
        return HandleResult(result, "Thu hồi quyền Administrator thành công.");
    }
}
