using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Administration.DTOs;
using StoryPlatform.Application.Features.Administration.Interfaces;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Features.AccessControl;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Administrator quản lý tài khoản Content Creator (Luồng 5, M5): xem danh sách, vô hiệu hoá, kích hoạt lại.
/// Duyệt đơn đăng ký Creator ở api/v1/admin/creator-applications.
/// </summary>
[HasPermission(Permissions.Admin.CreatorAccountManage)]
[Route("api/v1/admin/creators")]
public class CreatorAccountController : BaseApiController
{
    private readonly ICreatorAccountService _service;

    public CreatorAccountController(ICreatorAccountService service)
    {
        _service = service;
    }

    /// <summary>Danh sách Creator; status = Registered, AwaitingApproval, Active hoặc Suspended; search theo tên, username, email.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<CreatorAccountDto>>>> List(
        [FromQuery] AccountStatus? status, [FromQuery] string? search,
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await _service.ListAsync(status, search, pageIndex, pageSize, cancellationToken);
        return HandleResult(result, "Lấy danh sách nhà sáng tạo nội dung thành công.");
    }

    [HttpGet("{creatorUserId:int}")]
    public async Task<ActionResult<ApiResponse<CreatorAccountDto>>> Get(int creatorUserId, CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(creatorUserId, cancellationToken);
        return HandleResult(result, "Lấy thông tin nhà sáng tạo nội dung thành công.");
    }

    /// <summary>
    /// Vô hiệu hoá Creator đang hoạt động (lý do bắt buộc): khoá đăng nhập, rút yêu cầu gửi duyệt đang mở.
    /// Truyện đã Published giữ nguyên.
    /// </summary>
    [HttpPost("{creatorUserId:int}/deactivate")]
    public async Task<ActionResult<ApiResponse<DeactivateCreatorResultDto>>> Deactivate(
        int creatorUserId, [FromBody] DeactivateCreatorRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _service.DeactivateAsync(GetCurrentUserId(), creatorUserId, request, cancellationToken);
        return HandleResult(result, "Đã vô hiệu hoá tài khoản nhà sáng tạo nội dung.");
    }

    [HttpPost("{creatorUserId:int}/reactivate")]
    public async Task<ActionResult<ApiResponse<CreatorAccountDto>>> Reactivate(int creatorUserId, CancellationToken cancellationToken)
    {
        var result = await _service.ReactivateAsync(GetCurrentUserId(), creatorUserId, cancellationToken);
        return HandleResult(result, "Đã kích hoạt lại tài khoản nhà sáng tạo nội dung.");
    }
}
