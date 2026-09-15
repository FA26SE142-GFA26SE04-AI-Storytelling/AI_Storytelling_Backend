using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.Interfaces;

namespace StoryPlatform.Api.Controllers;

public class ChildAccessCredentialController : BaseApiController
{
    private readonly IChildAccessCredentialService _childAccessCredentialService;

    public ChildAccessCredentialController(IChildAccessCredentialService childAccessCredentialService)
    {
        _childAccessCredentialService = childAccessCredentialService;
    }

    /// <summary>
    /// Đặt/đổi PIN + avatar cho Child Access Credential (Bước 1.10) — chỉ Supervisor được làm, Trẻ không tự đổi được.
    /// </summary>
    [HttpPut("{childProfileId:int}")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<object?>>> SetPin(
        int childProfileId,
        [FromBody] SetChildAccessCredentialRequestDto request,
        CancellationToken cancellationToken)
    {
        await _childAccessCredentialService.SetPinAsync(
            childProfileId, GetCurrentUserId(), request, cancellationToken);

        return HandleResult<object?>(null, "Cập nhật PIN truy cập của trẻ thành công.");
    }

    /// <summary>
    /// Lấy thông tin Child Access Credential (KHÔNG bao giờ trả PIN/PinHash).
    /// </summary>
    [HttpGet("{childProfileId:int}")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<ChildAccessCredentialDto>>> GetCredential(
        int childProfileId, CancellationToken cancellationToken)
    {
        var result = await _childAccessCredentialService.GetCredentialAsync(childProfileId, GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy thông tin truy cập của trẻ thành công.");
    }

    /// <summary>
    /// Thu hồi Child Access Credential — Trẻ mất lối vào độc lập cho tới khi được thiết lập PIN mới.
    /// </summary>
    [HttpDelete("{childProfileId:int}")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<object?>>> RevokeCredential(
        int childProfileId, CancellationToken cancellationToken)
    {
        await _childAccessCredentialService.RevokeCredentialAsync(childProfileId, GetCurrentUserId(), cancellationToken);
        return HandleResult<object?>(null, "Thu hồi quyền truy cập độc lập của trẻ thành công.");
    }

    /// <summary>
    /// Trẻ tự đăng nhập bằng PIN, độc lập với phiên Supervisor (Bước 1.10) — KHÔNG cần JWT.
    /// </summary>
    [HttpPost("{childProfileId:int}/login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<ChildSessionDto>>> LoginWithPin(
        int childProfileId,
        [FromBody] LoginWithPinRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _childAccessCredentialService.LoginWithPinAsync(
            childProfileId, request.Pin, cancellationToken);

        return HandleResult(result, "Đăng nhập Child Session thành công.");
    }

    /// <summary>
    /// Trẻ đã đăng nhập bằng Child Access Token tự lấy thông tin hồ sơ của mình.
    /// </summary>
    [HttpGet("me")]
    [Authorize(Policy = "ChildSession")]
    public async Task<ActionResult<ApiResponse<ChildSessionProfileDto>>> GetMySession(
        CancellationToken cancellationToken)
    {
        var result = await _childAccessCredentialService.GetMySessionProfileAsync(
            GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy thông tin phiên của trẻ thành công.");
    }
}
