using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.CreatorApplications.DTOs;
using StoryPlatform.Application.Features.CreatorApplications.Interfaces;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Đơn đăng ký Content Creator (Luồng 5, M5). Creator đăng ký qua POST api/v1/Auth/register-creator; sau khi xác thực email,
/// tài khoản đăng nhập với role tạm CreatorApplicant và chỉ xem / nộp lại đơn ở đây. Administrator duyệt hoặc từ chối.
/// </summary>
[Route("api/v1/creator-applications")]
public class CreatorApplicationController : BaseApiController
{
    private readonly ICreatorApplicationService _service;

    public CreatorApplicationController(ICreatorApplicationService service)
    {
        _service = service;
    }

    /// <summary>Đơn mới nhất của Creator đang đăng nhập: chờ duyệt, đã duyệt, hoặc bị từ chối kèm lý do.</summary>
    [HttpGet("me")]
    [Authorize(Roles = CreatorApplicantAccess.ApplicantRole + ",ContentCreator")]
    public async Task<ActionResult<ApiResponse<CreatorApplicationDto>>> GetMine(CancellationToken cancellationToken)
    {
        var result = await _service.GetMineAsync(GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy đơn đăng ký Content Creator thành công.");
    }

    /// <summary>Nộp lại đơn sau khi bị từ chối; tạo đơn chờ duyệt mới.</summary>
    [HttpPost("me/resubmit")]
    [Authorize(Roles = CreatorApplicantAccess.ApplicantRole)]
    public async Task<ActionResult<ApiResponse<CreatorApplicationDto>>> Resubmit(
        [FromBody] ResubmitCreatorApplicationRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _service.ResubmitAsync(GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Đã nộp lại đơn đăng ký Content Creator.");
    }

    /// <summary>Danh sách đơn; status = Pending, Approved hoặc Rejected để lọc (Pending: cũ nhất trước).</summary>
    [HttpGet("/api/v1/admin/creator-applications")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<PagedResult<CreatorApplicationDto>>>> List(
        [FromQuery] CreatorApplicationStatus? status, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.ListAsync(status, pageIndex, pageSize, cancellationToken);
        return HandleResult(result, "Lấy danh sách đơn đăng ký Content Creator thành công.");
    }

    [HttpGet("/api/v1/admin/creator-applications/{applicationId:int}")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<CreatorApplicationDto>>> Get(
        int applicationId, CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(applicationId, cancellationToken);
        return HandleResult(result, "Lấy đơn đăng ký Content Creator thành công.");
    }

    /// <summary>Duyệt đơn: tài khoản Creator thành Active, Creator nhận thông báo và email.</summary>
    [HttpPost("/api/v1/admin/creator-applications/{applicationId:int}/approve")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<CreatorApplicationDto>>> Approve(
        int applicationId, CancellationToken cancellationToken)
    {
        var result = await _service.ApproveAsync(GetCurrentUserId(), applicationId, cancellationToken);
        return HandleResult(result, "Đã duyệt đơn đăng ký Content Creator.");
    }

    /// <summary>Từ chối đơn kèm lý do bắt buộc; Creator thấy lý do và có thể nộp lại.</summary>
    [HttpPost("/api/v1/admin/creator-applications/{applicationId:int}/reject")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<CreatorApplicationDto>>> Reject(
        int applicationId, [FromBody] RejectCreatorApplicationRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _service.RejectAsync(GetCurrentUserId(), applicationId, request, cancellationToken);
        return HandleResult(result, "Đã từ chối đơn đăng ký Content Creator.");
    }
}
