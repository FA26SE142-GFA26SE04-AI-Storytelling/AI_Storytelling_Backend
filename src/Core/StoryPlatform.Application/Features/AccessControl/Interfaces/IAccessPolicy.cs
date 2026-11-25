using StoryPlatform.Application.Features.AccessControl.DTOs;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.AccessControl.Interfaces;

/// <summary>
/// Cổng kiểm quyền dùng chung trong service: tài khoản còn hoạt động → role có permission → điều kiện gắn với lần cấp
/// quyền đó. Endpoint vẫn chặn trước bằng <c>[HasPermission]</c>; cổng này kiểm thêm theo bản ghi đang thao tác.
/// </summary>
public interface IAccessPolicy
{
    /// <param name="resource">Bản ghi đang thao tác (hiện dùng <see cref="ModerationItem"/>); null khi không trên bản ghi cụ thể.</param>
    /// <param name="deniedMessage">Message khi tài khoản không hợp lệ hoặc role thiếu permission; giữ message cũ của service.</param>
    /// <returns>Tài khoản thao tác đã nạp.</returns>
    Task<UserAccount> EnsureAsync(
        int actorUserId,
        string permission,
        object? resource = null,
        AccessMode mode = AccessMode.Write,
        string? deniedMessage = null,
        CancellationToken cancellationToken = default);

    /// <summary>Role có đang giữ quyền không, không xét điều kiện; dùng để lọc danh sách.</summary>
    Task<bool> RoleHoldsAsync(string roleCode, string permission, CancellationToken cancellationToken = default);

    /// <summary>
    /// Các quyền role đang giữ kèm trạng thái lúc này, tính từ chính các điều kiện mà <see cref="EnsureAsync"/> chặn,
    /// để giao diện ẩn hoặc khoá nút kèm lý do. Chỉ phục vụ hiển thị.
    /// </summary>
    Task<IReadOnlyList<PermissionAccessDto>> DescribeAsync(string roleCode, CancellationToken cancellationToken = default);
}
