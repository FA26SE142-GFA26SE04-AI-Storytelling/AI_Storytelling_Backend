using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.AuditLogs.DTOs;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;

namespace StoryPlatform.Api.Controllers;

public class ChildProfileController : BaseApiController
{
    private readonly IChildProfileService _childProfileService;
    private readonly IAuditLogQueryService _auditLogQueryService;

    public ChildProfileController(
        IChildProfileService childProfileService,
        IAuditLogQueryService auditLogQueryService)
    {
        _childProfileService = childProfileService;
        _auditLogQueryService = auditLogQueryService;
    }

    /// <summary>
    /// Tạo hồ sơ trẻ và quan hệ giám sát Owner trong cùng giao dịch.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<ChildProfileDto>>> CreateChildProfile(
        [FromBody] CreateChildProfileRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _childProfileService.CreateChildProfileAsync(
            GetCurrentUserId(), request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<ChildProfileDto>.Ok(result, "Tạo hồ sơ trẻ thành công."));
    }

    /// <summary>
    /// Danh sách hồ sơ trẻ do chính tài khoản đang đăng nhập làm Owner (Bước 1.9a — ProfileSwitcher).
    /// </summary>
    [HttpGet("mine")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<List<ChildProfileDto>>>> ListMyChildProfiles(
        CancellationToken cancellationToken)
    {
        var result = await _childProfileService.ListMyChildProfilesAsync(GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy danh sách hồ sơ trẻ thành công.");
    }

    /// <summary>
    /// Lấy chi tiết 1 hồ sơ trẻ.
    /// </summary>
    [HttpGet("{id:int}")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<ChildProfileDto>>> GetChildProfileById(
        int id, CancellationToken cancellationToken)
    {
        var result = await _childProfileService.GetChildProfileByIdAsync(id, GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy chi tiết hồ sơ trẻ thành công.");
    }

    /// <summary>
    /// Lịch sử thay đổi hồ sơ trẻ; chỉ supervisor còn hiệu lực được xem.
    /// </summary>
    [HttpGet("{id:int}/audit-log")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditLogDto>>>> GetChildProfileAuditLog(
        int id, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var result = await _auditLogQueryService.GetChildProfileAuditLogAsync(
            id, GetCurrentUserId(), pageRequest, cancellationToken);
        return HandleResult(result, "Lấy lịch sử thay đổi hồ sơ trẻ thành công.");
    }

    /// <summary>
    /// Cập nhật Nickname/AgeBand/Language của hồ sơ trẻ (KHÔNG đổi scope/organization).
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<ChildProfileDto>>> UpdateChildProfile(
        int id, [FromBody] UpdateChildProfileRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _childProfileService.UpdateChildProfileAsync(id, GetCurrentUserId(), request, cancellationToken);
        return HandleResult(result, "Cập nhật hồ sơ trẻ thành công.");
    }

    /// <summary>
    /// Kích hoạt hồ sơ trẻ (Bước 1.8) — Active nếu thỏa BR-1.9, ngược lại Pending Parent Consent.
    /// </summary>
    [HttpPatch("{id:int}/activate")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<ChildProfileDto>>> ActivateChildProfile(
        int id, CancellationToken cancellationToken)
    {
        var result = await _childProfileService.ActivateChildProfileAsync(id, GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Xử lý kích hoạt hồ sơ trẻ thành công.");
    }

    /// <summary>
    /// Lưu trữ (archive) hồ sơ trẻ — dừng truy cập mới, giữ nguyên lịch sử. Chỉ Owner được thực hiện.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<object?>>> ArchiveChildProfile(
        int id, CancellationToken cancellationToken)
    {
        await _childProfileService.ArchiveChildProfileAsync(id, GetCurrentUserId(), cancellationToken);
        return HandleResult<object?>(null, "Lưu trữ hồ sơ trẻ thành công.");
    }
}
