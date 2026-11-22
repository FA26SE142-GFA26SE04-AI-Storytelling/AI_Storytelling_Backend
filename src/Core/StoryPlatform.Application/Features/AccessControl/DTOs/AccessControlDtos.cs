namespace StoryPlatform.Application.Features.AccessControl.DTOs;

public sealed record PermissionDto(string Code, string Group, string Description, bool IsStaffGrantable);

public sealed record RoleDto(string Code, string Name, bool IsStaff);

public sealed record RolePermissionsDto(string RoleCode, IReadOnlyList<string> PermissionCodes);

public sealed class UpdateRolePermissionsRequest
{
    /// <summary>Toàn bộ permission nhân viên (nhóm Kiểm duyệt, Quản trị) role sẽ giữ; quyền ngoài hai nhóm này giữ nguyên.</summary>
    public List<string>? PermissionCodes { get; set; }
}
