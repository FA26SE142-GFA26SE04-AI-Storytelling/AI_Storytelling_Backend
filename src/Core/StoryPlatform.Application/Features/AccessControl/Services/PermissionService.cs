using StoryPlatform.Application.Abstractions.Caching;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AccessControl.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.AccessControl.Services;

/// <summary>
/// Đọc quyền của role từ bảng role_permissions, cache 60 giây ở Redis (dùng chung giữa các instance; Administrator
/// sửa quyền thì xóa khóa). Role chưa có dòng trong bảng roles (DB mới, seeder lỗi) → dùng bản gán mặc định trong code
/// để hệ thống không chặn mọi request.
/// </summary>
public class PermissionService(IUnitOfWork uow, IAppCache? cache = null) : IPermissionService
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly Dictionary<string, IReadOnlySet<string>> _requestMemo = new(StringComparer.Ordinal);

    public static string CacheKey(string roleCode) => $"access:role-permissions:{roleCode}";

    public async Task<bool> RoleHasAsync(string roleCode, string permissionCode, CancellationToken cancellationToken = default) =>
        (await GetForRoleAsync(roleCode, cancellationToken)).Contains(permissionCode);

    public async Task<IReadOnlySet<string>> GetForRoleAsync(string roleCode, CancellationToken cancellationToken = default)
    {
        if (_requestMemo.TryGetValue(roleCode, out var memo)) return memo;

        var cached = await cache.GetOrSetOrLoadAsync(CacheKey(roleCode), ct => LoadAsync(roleCode, ct), CacheTtl, cancellationToken);
        IReadOnlySet<string> codes = cached.RoleFound
            ? new HashSet<string>(cached.Codes, StringComparer.Ordinal)
            : DefaultRolePermissions.Map.TryGetValue(roleCode, out var defaults) ? defaults : new HashSet<string>();
        _requestMemo[roleCode] = codes;
        return codes;
    }

    public async Task InvalidateAsync(string roleCode, CancellationToken cancellationToken = default)
    {
        _requestMemo.Remove(roleCode);
        await cache.RemoveIfConfiguredAsync(CacheKey(roleCode), cancellationToken);
    }

    private async Task<CachedRolePermissions> LoadAsync(string roleCode, CancellationToken ct)
    {
        var role = await uow.Repository<Role>().FirstOrDefaultAsync(r => r.Code == roleCode && !r.IsDeleted, cancellationToken: ct);
        if (role is null) return new CachedRolePermissions(false, []);

        var links = await uow.Repository<RolePermission>().FindAsync(
            x => x.RoleId == role.Id && !x.Permission.IsDeleted, includeProperties: nameof(RolePermission.Permission), cancellationToken: ct);
        return new CachedRolePermissions(true, links.Select(x => x.Permission.Code).Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Giá trị lưu cache; <c>RoleFound = false</c> nghĩa là dùng bản gán mặc định.</summary>
    public sealed record CachedRolePermissions(bool RoleFound, string[] Codes);
}
