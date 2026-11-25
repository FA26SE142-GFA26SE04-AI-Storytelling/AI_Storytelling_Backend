using StoryPlatform.Application.Features.AccessControl.DTOs;

namespace StoryPlatform.Application.Features.AccessControl.Interfaces;

/// <summary>Administrator xem danh mục quyền và chỉnh quyền của mọi role người lớn.</summary>
public interface IRolePermissionAdminService
{
    Task<IReadOnlyList<PermissionDto>> ListPermissionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken cancellationToken = default);

    Task<RolePermissionsDto> GetRolePermissionsAsync(string roleCode, CancellationToken cancellationToken = default);

    Task<RolePermissionsDto> UpdateRolePermissionsAsync(int actorUserId, string roleCode, UpdateRolePermissionsRequest request,
        CancellationToken cancellationToken = default);
}
