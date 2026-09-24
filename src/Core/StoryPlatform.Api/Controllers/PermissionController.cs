using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Danh mục quyền hệ thống và quyền được cấp cho quan hệ giám sát.
/// </summary>
public class PermissionController : BaseApiController
{
    private readonly ISupervisionService _supervisionService;

    public PermissionController(ISupervisionService supervisionService)
    {
        _supervisionService = supervisionService;
    }

    /// <summary>
    /// Lấy danh sách toàn bộ quyền có trong hệ thống.
    /// </summary>
    [HttpGet]
    [Authorize]
    public ActionResult<ApiResponse<List<string>>> ListSystemPermissions()
    {
        var permissions = Enum.GetValues<Permission>().Select(permission => permission.ToString()).ToList();
        return HandleResult(permissions, "Lấy danh sách permission hệ thống thành công.");
    }

    /// <summary>
    /// Lấy danh sách quyền của một quan hệ giám sát.
    /// </summary>
    [HttpGet("relationships/{relationshipId:int}")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<List<string>>>> ListRelationshipPermissions(
        int relationshipId, CancellationToken cancellationToken)
    {
        var result = await _supervisionService.ListPermissionsAsync(
            relationshipId, GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy danh sách quyền đã cấp thành công.");
    }

    /// <summary>
    /// Cấp một quyền cho quan hệ giám sát.
    /// </summary>
    [HttpPost("relationships/{relationshipId:int}/{permission}")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<object?>>> GrantPermission(
        int relationshipId, Permission permission, CancellationToken cancellationToken)
    {
        await _supervisionService.GrantPermissionAsync(
            relationshipId, GetCurrentUserId(), permission, cancellationToken);
        return HandleResult<object?>(null, "Cấp quyền thành công.");
    }

    /// <summary>
    /// Thu hồi một quyền khỏi quan hệ giám sát.
    /// </summary>
    [HttpDelete("relationships/{relationshipId:int}/{permission}")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<object?>>> RevokePermission(
        int relationshipId, Permission permission, CancellationToken cancellationToken)
    {
        await _supervisionService.RevokePermissionAsync(
            relationshipId, GetCurrentUserId(), permission, cancellationToken);
        return HandleResult<object?>(null, "Thu hồi quyền thành công.");
    }

    /// <summary>
    /// Tạo yêu cầu xin cấp quyền cho quan hệ giám sát của chính Additional Supervisor.
    /// </summary>
    [HttpPost("relationships/{relationshipId:int}/requests")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<PermissionRequestDto>>> CreatePermissionRequest(
        int relationshipId,
        [FromBody] CreatePermissionRequestRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _supervisionService.CreatePermissionRequestAsync(
            relationshipId, GetCurrentUserId(), request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<PermissionRequestDto>.Ok(result, "Tạo yêu cầu xin quyền thành công."));
    }

    /// <summary>
    /// Danh sách yêu cầu xin quyền của một quan hệ giám sát.
    /// </summary>
    [HttpGet("relationships/{relationshipId:int}/requests")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<List<PermissionRequestDto>>>> ListPermissionRequests(
        int relationshipId, CancellationToken cancellationToken)
    {
        var result = await _supervisionService.ListPermissionRequestsAsync(
            relationshipId, GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy danh sách yêu cầu xin quyền thành công.");
    }

    /// <summary>
    /// Owner chấp nhận yêu cầu xin quyền — cấp toàn bộ permission trong yêu cầu.
    /// </summary>
    [HttpPost("requests/{permissionRequestId:int}/accept")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<PermissionRequestDto>>> AcceptPermissionRequest(
        int permissionRequestId, CancellationToken cancellationToken)
    {
        var result = await _supervisionService.AcceptPermissionRequestAsync(
            permissionRequestId, GetCurrentUserId(), cancellationToken);

        return HandleResult(result, "Chấp nhận yêu cầu xin quyền thành công.");
    }

    /// <summary>
    /// Owner từ chối yêu cầu xin quyền.
    /// </summary>
    [HttpPost("requests/{permissionRequestId:int}/reject")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<PermissionRequestDto>>> RejectPermissionRequest(
        int permissionRequestId, CancellationToken cancellationToken)
    {
        var result = await _supervisionService.RejectPermissionRequestAsync(
            permissionRequestId, GetCurrentUserId(), cancellationToken);

        return HandleResult(result, "Từ chối yêu cầu xin quyền thành công.");
    }

    /// <summary>
    /// Additional Supervisor tự huỷ yêu cầu xin quyền do chính mình tạo, khi Owner chưa xử lý (BR-1.14).
    /// </summary>
    [HttpPost("requests/{permissionRequestId:int}/cancel")]
    [Authorize(Roles = "Parent,Teacher")]
    public async Task<ActionResult<ApiResponse<PermissionRequestDto>>> CancelPermissionRequest(
        int permissionRequestId, CancellationToken cancellationToken)
    {
        var result = await _supervisionService.CancelPermissionRequestAsync(
            permissionRequestId, GetCurrentUserId(), cancellationToken);

        return HandleResult(result, "Huỷ yêu cầu xin quyền thành công.");
    }
}
