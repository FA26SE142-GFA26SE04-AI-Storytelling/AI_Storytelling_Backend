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
    /// Trạng thái Easy Login của hồ sơ (đã có chưa, tạo lúc nào) — KHÔNG bao giờ trả secret hay hash.
    /// </summary>
    [HttpGet("{childProfileId:int}")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<ChildAccessCredentialDto>>> GetCredential(
        int childProfileId, CancellationToken cancellationToken)
    {
        var result = await _childAccessCredentialService.GetCredentialAsync(childProfileId, GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy thông tin truy cập của trẻ thành công.");
    }

    /// <summary>
    /// Thu hồi Easy Login (soft delete): trẻ bị đăng xuất ngay và mất lối vào độc lập cho tới khi tạo Easy Login mới.
    /// Cần quyền manage_safety_settings (Owner luôn có).
    /// </summary>
    [HttpDelete("{childProfileId:int}")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<object?>>> RevokeCredential(
        int childProfileId, CancellationToken cancellationToken)
    {
        await _childAccessCredentialService.RevokeCredentialAsync(childProfileId, GetCurrentUserId(), cancellationToken);
        return HandleResult<object?>(null, "Thu hồi quyền truy cập độc lập của trẻ thành công.");
    }

    /// <summary>
    /// Tạo Easy Login lâu dài (lần đầu) hoặc TẠO LẠI secret mới (secret cũ vô hiệu ngay, mọi phiên của trẻ từ secret cũ bị kết thúc).
    /// Secret chỉ trả đúng một lần — client dựng QR từ giá trị này. Cần quyền manage_safety_settings (Owner luôn có).
    /// </summary>
    [HttpPost("{childProfileId:int}/easylogin")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<EasyLoginSecretDto>>> CreateOrRegenerateEasyLogin(
        int childProfileId, CancellationToken cancellationToken)
    {
        var result = await _childAccessCredentialService.CreateOrRegenerateEasyLoginAsync(
            childProfileId, GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Đã tạo Easy Login mới.");
    }

    /// <summary>
    /// Supervisor bàn giao thiết bị cho trẻ sau khi chọn hồ sơ qua ProfileSwitcher (Luồng 3, Bước 3.0) —
    /// phát sinh Child Session riêng, tách khỏi phiên Supervisor.
    /// </summary>
    [HttpPost("{childProfileId:int}/handover")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<ChildSessionDto>>> StartSupervisedSession(
        int childProfileId,
        [FromBody] StartSupervisedChildSessionRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _childAccessCredentialService.StartSupervisedSessionAsync(
            childProfileId, GetCurrentUserId(), request.SupervisorRefreshToken, cancellationToken);
        return HandleResult(result, "Đã bàn giao thiết bị cho trẻ.");
    }

    /// <summary>
    /// Trẻ quét QR Easy Login để vào Child Session, không cần phiên Supervisor.
    /// Quét sai 5 lần liên tiếp trên cùng thiết bị (IP client) thì khóa 5 phút.
    /// </summary>
    [HttpPost("easylogin/login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<ChildSessionDto>>> LoginWithEasyLogin(
        [FromBody] LoginWithEasyLoginRequestDto request,
        CancellationToken cancellationToken)
    {
        var clientKey = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _childAccessCredentialService.LoginWithEasyLoginAsync(
            request.Secret, clientKey, cancellationToken);
        return HandleResult(result, "Đăng nhập bằng EasyLogin thành công.");
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
