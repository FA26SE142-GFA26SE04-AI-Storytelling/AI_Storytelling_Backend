using StoryPlatform.Application.Features.AccessControl.Interfaces;

namespace StoryPlatform.Application.Features.AccessControl.Services;

/// <summary>Đọc bản gán mặc định trong code; dùng khi chưa có DB (test, khởi tạo) và làm phương án dự phòng.</summary>
public class StaticPermissionService : IPermissionService
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

    public Task<bool> RoleHasAsync(string roleCode, string permissionCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(Lookup(roleCode).Contains(permissionCode));

    public Task<IReadOnlySet<string>> GetForRoleAsync(string roleCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(Lookup(roleCode));

    public Task InvalidateAsync(string roleCode, CancellationToken cancellationToken = default) => Task.CompletedTask;

    private static IReadOnlySet<string> Lookup(string roleCode) =>
        DefaultRolePermissions.Map.TryGetValue(roleCode, out var codes) ? codes : Empty;
}
