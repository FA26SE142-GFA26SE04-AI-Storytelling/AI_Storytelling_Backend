namespace StoryPlatform.Application.Features.AccessControl.DTOs;

/// <param name="Scope">Phạm vi áp quyền: <c>Any</c>, <c>Own</c> hoặc <c>Assigned</c> (mục đang do chính mình nhận).</param>
public sealed record PermissionDto(string Code, string Group, string Description, bool IsStaffGrantable, string Scope);

/// <param name="Id">Id của role, khớp <c>audit_logs.EntityId</c> của thao tác RolePermissionsUpdated.</param>
public sealed record RoleDto(int Id, string Code, string Name, bool IsStaff);

/// <param name="AssignableCodes">
/// Quyền role này được bật: bản gán mặc định của role cộng các quyền role đang giữ. Cấp quyền ngoài tập này
/// bị từ chối vì service vẫn kiểm role (giai đoạn 3 mới đọc permission).
/// </param>
/// <param name="Conditions">Điều kiện kèm theo khi role này dùng quyền (câu tiếng Việt), chỉ gồm quyền có điều kiện; định nghĩa trong code, chỉ đọc.</param>
/// <param name="DefaultCodes">Bản gán mặc định của role; khác tập đang giữ nghĩa là Administrator đã thu hoặc trả quyền.</param>
/// <param name="Scopes">Phạm vi theo role cho từng quyền assignable: <c>Any</c>, <c>Own</c> hoặc <c>Assigned</c>.</param>
/// <param name="ConditionCodes">Mã điều kiện theo quyền, cùng thứ tự với <paramref name="Conditions"/>.</param>
public sealed record RolePermissionsDto(string RoleCode, IReadOnlyList<string> PermissionCodes, IReadOnlyList<string> AssignableCodes,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Conditions, IReadOnlyList<string> DefaultCodes,
    IReadOnlyDictionary<string, string> Scopes, IReadOnlyDictionary<string, IReadOnlyList<string>> ConditionCodes);

public sealed class UpdateRolePermissionsRequest
{
    /// <summary>Toàn bộ permission role sẽ giữ sau khi lưu (thay cả tập).</summary>
    public List<string>? PermissionCodes { get; set; }
}
