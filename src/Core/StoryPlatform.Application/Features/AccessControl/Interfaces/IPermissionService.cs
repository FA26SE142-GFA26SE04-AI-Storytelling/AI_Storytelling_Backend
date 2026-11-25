namespace StoryPlatform.Application.Features.AccessControl.Interfaces;

/// <summary>Trả lời "role này có permission này không" cho policy <c>[HasPermission]</c>.</summary>
public interface IPermissionService
{
    Task<bool> RoleHasAsync(string roleCode, string permissionCode, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<string>> GetForRoleAsync(string roleCode, CancellationToken cancellationToken = default);

    /// <summary>Xóa bản cache quyền của role sau khi Administrator sửa quyền.</summary>
    Task InvalidateAsync(string roleCode, CancellationToken cancellationToken = default);
}
