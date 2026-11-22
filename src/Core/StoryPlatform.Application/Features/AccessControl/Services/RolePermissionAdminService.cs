using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AccessControl.DTOs;
using StoryPlatform.Application.Features.AccessControl.Interfaces;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.AccessControl.Services;

public class RolePermissionAdminService(IUnitOfWork uow, IAuditLogWriter audit, IPermissionService permissions)
    : IRolePermissionAdminService
{
    public const string AuditAction = "RolePermissionsUpdated";

    /// <summary>Mỗi bước duyệt nội dung dành cho trẻ phải luôn còn ít nhất một role nhân viên giữ quyền.</summary>
    public static readonly IReadOnlyList<string> ReviewSteps =
    [
        Permissions.Moderation.QueueProcess,
        Permissions.Moderation.PublishedReview,
        Permissions.Review.PublicStory,
        Permissions.Review.GrayZone,
        Permissions.Review.Roadmap,
        Permissions.Review.CreatorPublication,
    ];

    public async Task<IReadOnlyList<PermissionDto>> ListPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var all = await uow.Repository<Permission>().FindAsync(p => !p.IsDeleted, cancellationToken: cancellationToken);
        return all
            .OrderBy(p => GroupIndex(p.Group)).ThenBy(p => p.Code, StringComparer.Ordinal)
            .Select(p => new PermissionDto(p.Code, p.Group, p.Description, p.IsStaffGrantable))
            .ToList();
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await uow.Repository<Role>().FindAsync(r => !r.IsDeleted, cancellationToken: cancellationToken);
        return roles
            .OrderBy(r => RoleIndex(r.Code)).ThenBy(r => r.Code, StringComparer.Ordinal)
            .Select(r => new RoleDto(r.Code, r.Name, r.IsStaff))
            .ToList();
    }

    public async Task<RolePermissionsDto> GetRolePermissionsAsync(string roleCode, CancellationToken cancellationToken = default)
    {
        var role = await FindRoleAsync(roleCode, cancellationToken);
        var links = await LinksAsync(role.Id, cancellationToken);
        return new RolePermissionsDto(role.Code, Codes(links));
    }

    public async Task<RolePermissionsDto> UpdateRolePermissionsAsync(int actorUserId, string roleCode,
        UpdateRolePermissionsRequest request, CancellationToken cancellationToken = default)
    {
        var role = await FindRoleAsync(roleCode, cancellationToken);
        if (!role.IsStaff)
            throw new ForbiddenException("Không được sửa quyền của role người dùng.");
        if (request.PermissionCodes is null)
            throw new BadRequestException("Danh sách quyền không hợp lệ.");

        var catalog = (await uow.Repository<Permission>().FindAsync(p => !p.IsDeleted, cancellationToken: cancellationToken))
            .ToDictionary(p => p.Code, StringComparer.Ordinal);
        var requested = request.PermissionCodes.Distinct(StringComparer.Ordinal).ToList();
        var invalid = requested
            .Where(code => !catalog.TryGetValue(code, out var p) || !p.IsStaffGrantable)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (invalid.Count > 0)
            throw new BadRequestException($"Quyền không hợp lệ hoặc không được gán cho nhân viên: {string.Join(", ", invalid)}.");

        var currentLinks = await LinksAsync(role.Id, cancellationToken);
        var before = Codes(currentLinks);
        // Quyền ngoài nhóm nhân viên (ví dụ xem thư viện cộng đồng) không sửa qua API nên giữ nguyên.
        var after = currentLinks.Where(x => !x.Permission.IsStaffGrantable).Select(x => x.Permission.Code)
            .Concat(requested).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        if (role.Code == RoleCodes.Administrator && !after.Contains(Permissions.Admin.PermissionManage))
            throw new ConflictException("Administrator phải giữ quyền quản lý phân quyền.", "PERMISSION_MANAGE_REQUIRED");

        await EnsureReviewersRemainAsync(role, after, cancellationToken);

        var linkRepo = uow.Repository<RolePermission>();
        linkRepo.DeleteRange(currentLinks.Select(x => new RolePermission { RoleId = x.RoleId, PermissionId = x.PermissionId }));
        await linkRepo.AddRangeAsync(after.Select(code => new RolePermission { RoleId = role.Id, PermissionId = catalog[code].Id }),
            cancellationToken);
        // Ghi log và các thay đổi liên kết trong cùng một SaveChanges của AuditLogWriter.
        await audit.LogAsync(actorUserId, AuditAction, "Role", role.Id, before, after, cancellationToken);
        await permissions.InvalidateAsync(role.Code, cancellationToken);

        return new RolePermissionsDto(role.Code, after);
    }

    private async Task EnsureReviewersRemainAsync(Role target, IReadOnlyCollection<string> targetAfter, CancellationToken ct)
    {
        var otherStaff = await uow.Repository<Role>().FindAsync(r => r.IsStaff && !r.IsDeleted && r.Id != target.Id, cancellationToken: ct);
        var otherIds = otherStaff.Select(r => r.Id).ToList();
        var otherCodes = (await uow.Repository<RolePermission>().FindAsync(
                x => otherIds.Contains(x.RoleId) && !x.Permission.IsDeleted,
                includeProperties: nameof(RolePermission.Permission), cancellationToken: ct))
            .Select(x => x.Permission.Code).ToHashSet(StringComparer.Ordinal);

        var orphan = ReviewSteps.FirstOrDefault(step => !targetAfter.Contains(step) && !otherCodes.Contains(step));
        if (orphan is not null)
            throw new ConflictException($"Phải còn ít nhất một role duyệt bước này: {orphan}.", "REVIEWER_REQUIRED");
    }

    private async Task<Role> FindRoleAsync(string roleCode, CancellationToken ct) =>
        await uow.Repository<Role>().FirstOrDefaultAsync(r => r.Code == roleCode && !r.IsDeleted, cancellationToken: ct)
        ?? throw new NotFoundException("Role", roleCode);

    private Task<IReadOnlyList<RolePermission>> LinksAsync(int roleId, CancellationToken ct) =>
        uow.Repository<RolePermission>().FindAsync(x => x.RoleId == roleId && !x.Permission.IsDeleted,
            includeProperties: nameof(RolePermission.Permission), cancellationToken: ct);

    private static List<string> Codes(IEnumerable<RolePermission> links) =>
        links.Select(x => x.Permission.Code).Order(StringComparer.Ordinal).ToList();

    private static int GroupIndex(string group)
    {
        var index = PermissionCatalog.GroupOrder.ToList().IndexOf(group);
        return index < 0 ? int.MaxValue : index;
    }

    private static int RoleIndex(string code)
    {
        var index = RoleCodes.All.ToList().IndexOf(code);
        return index < 0 ? int.MaxValue : index;
    }
}
