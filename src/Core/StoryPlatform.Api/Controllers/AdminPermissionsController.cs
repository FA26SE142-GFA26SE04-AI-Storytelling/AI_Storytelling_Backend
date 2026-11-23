using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AccessControl;
using StoryPlatform.Application.Features.AccessControl.DTOs;
using StoryPlatform.Application.Features.AccessControl.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Administrator xem danh mục quyền và chỉnh quyền của mọi role người lớn (thu hoặc trả lại quyền mặc định).
/// Phiên của trẻ không dùng permission.
/// </summary>
[Route("api/v1/admin")]
[HasPermission(Permissions.Admin.PermissionManage)]
public class AdminPermissionsController(IRolePermissionAdminService service) : BaseApiController
{
    /// <summary>Danh mục permission, theo nhóm.</summary>
    [HttpGet("permissions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PermissionDto>>>> ListPermissions(CancellationToken cancellationToken) =>
        HandleResult(await service.ListPermissionsAsync(cancellationToken), "Lấy danh mục quyền thành công.");

    /// <summary>Danh sách role.</summary>
    [HttpGet("roles")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoleDto>>>> ListRoles(CancellationToken cancellationToken) =>
        HandleResult(await service.ListRolesAsync(cancellationToken), "Lấy danh sách role thành công.");

    /// <summary>Các permission đang gán cho một role.</summary>
    [HttpGet("roles/{code}/permissions")]
    public async Task<ActionResult<ApiResponse<RolePermissionsDto>>> GetRolePermissions(string code, CancellationToken cancellationToken) =>
        HandleResult(await service.GetRolePermissionsAsync(code, cancellationToken), "Lấy quyền của role thành công.");

    /// <summary>Thay toàn bộ permission của một role; ghi nhật ký thao tác.</summary>
    [HttpPut("roles/{code}/permissions")]
    public async Task<ActionResult<ApiResponse<RolePermissionsDto>>> UpdateRolePermissions(
        string code, [FromBody] UpdateRolePermissionsRequest request, CancellationToken cancellationToken) =>
        HandleResult(await service.UpdateRolePermissionsAsync(GetCurrentUserId(), code, request, cancellationToken),
            "Cập nhật quyền thành công.");
}
