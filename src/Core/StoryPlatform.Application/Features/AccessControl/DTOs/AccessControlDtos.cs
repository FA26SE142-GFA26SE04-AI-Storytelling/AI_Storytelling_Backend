namespace StoryPlatform.Application.Features.AccessControl.DTOs;

public sealed record PermissionDto(string Code, string Group, string Description, bool IsStaffGrantable);

public sealed record RoleDto(string Code, string Name, bool IsStaff);

/// <param name="AssignableCodes">
/// Quyền role này được bật: bản gán mặc định của role cộng các quyền role đang giữ. Cấp quyền ngoài tập này
/// bị từ chối vì service vẫn kiểm role (giai đoạn 3 mới đọc permission).
/// </param>
public sealed record RolePermissionsDto(string RoleCode, IReadOnlyList<string> PermissionCodes, IReadOnlyList<string> AssignableCodes);

public sealed class UpdateRolePermissionsRequest
{
    /// <summary>Toàn bộ permission role sẽ giữ sau khi lưu (thay cả tập).</summary>
    public List<string>? PermissionCodes { get; set; }
}
