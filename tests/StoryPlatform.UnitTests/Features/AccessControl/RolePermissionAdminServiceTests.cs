using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AccessControl;
using StoryPlatform.Application.Features.AccessControl.DTOs;
using StoryPlatform.Application.Features.AccessControl.Interfaces;
using StoryPlatform.Application.Features.AccessControl.Services;
using StoryPlatform.Application.Features.AuditLogs.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.UnitTests.Features.AccessControl;

public sealed class RolePermissionAdminServiceTests : IAsyncLifetime
{
    private const int AdminUserId = 7;
    private SqliteConnection _connection = null!;
    private ApplicationDbContext _db = null!;
    private readonly RecordingPermissionService _permissions = new();

    public async Task InitializeAsync()
    {
        (_connection, _db) = await AccessControlTestDb.CreateAsync();
        await new PermissionCatalogSeeder(new UnitOfWork(_db)).SeedAsync(CancellationToken.None);
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private RolePermissionAdminService Service()
    {
        var uow = new UnitOfWork(_db);
        return new RolePermissionAdminService(uow, new AuditLogWriter(uow), _permissions);
    }

    private static UpdateRolePermissionsRequest Request(params string[] codes) => new() { PermissionCodes = codes.ToList() };

    private Task<List<string>> CodesOf(string role) => _db.Set<RolePermission>().AsNoTracking()
        .Where(x => x.Role.Code == role).Select(x => x.Permission.Code).OrderBy(x => x).ToListAsync();

    private static string[] StaffDefaults(string role) => DefaultRolePermissions.Map[role]
        .Where(code => PermissionCatalog.Find(code)!.IsStaffGrantable).ToArray();

    [Fact]
    public async Task Lists_permissions_in_group_order_and_staff_roles_flagged()
    {
        var permissions = await Service().ListPermissionsAsync();
        Assert.Equal(PermissionCatalog.All.Count, permissions.Count);
        Assert.Equal(PermissionCatalog.GroupOrder, permissions.Select(p => p.Group).Distinct());

        var roles = await Service().ListRolesAsync();
        Assert.Equal(RoleCodes.All, roles.Select(r => r.Code));
        Assert.Equal(RoleCodes.Staff, roles.Where(r => r.IsStaff).Select(r => r.Code));
    }

    [Fact]
    public async Task Update_replaces_set_writes_audit_and_invalidates()
    {
        var target = StaffDefaults(RoleCodes.Moderator).Where(c => c != Permissions.Moderation.CategoryManage)
            .Append(Permissions.Admin.AuditView).ToArray();

        var result = await Service().UpdateRolePermissionsAsync(AdminUserId, RoleCodes.Moderator, Request(target));
        _db.ChangeTracker.Clear();

        var expected = DefaultRolePermissions.Map[RoleCodes.Moderator]
            .Where(c => !PermissionCatalog.Find(c)!.IsStaffGrantable) // quyền không phải nhân viên giữ nguyên
            .Concat(target).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expected, await CodesOf(RoleCodes.Moderator));
        Assert.Equal(expected, result.PermissionCodes);
        var log = await _db.Set<AuditLog>().AsNoTracking().SingleAsync();
        Assert.Equal("RolePermissionsUpdated", log.Action);
        Assert.Equal("Role", log.EntityType);
        Assert.Equal(AdminUserId, log.ActorUserId);
        Assert.Contains(Permissions.Moderation.CategoryManage, log.BeforeState);
        Assert.DoesNotContain(Permissions.Moderation.CategoryManage, log.AfterState);
        Assert.Contains(RoleCodes.Moderator, _permissions.Invalidated);
    }

    [Fact]
    public async Task Non_staff_role_is_forbidden()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service().UpdateRolePermissionsAsync(AdminUserId, RoleCodes.User, Request()));
        Assert.Equal("Không được sửa quyền của role người dùng.", ex.Message);
    }

    [Fact]
    public async Task Unknown_or_wrong_case_role_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service().UpdateRolePermissionsAsync(AdminUserId, "moderator", Request()));
        await Assert.ThrowsAsync<NotFoundException>(() => Service().GetRolePermissionsAsync("Ghost"));
    }

    [Fact]
    public async Task Null_list_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            Service().UpdateRolePermissionsAsync(AdminUserId, RoleCodes.Moderator, new UpdateRolePermissionsRequest { PermissionCodes = null }));
        Assert.Equal("Danh sách quyền không hợp lệ.", ex.Message);
    }

    [Fact]
    public async Task Non_grantable_or_unknown_codes_are_rejected_with_list()
    {
        var ex = await Assert.ThrowsAsync<BadRequestException>(() => Service().UpdateRolePermissionsAsync(
            AdminUserId, RoleCodes.Moderator, Request([.. StaffDefaults(RoleCodes.Moderator), Permissions.Parent.StoryAuthor, "x.y", "REVIEW.GRAY_ZONE"])));
        Assert.Equal("Quyền không hợp lệ hoặc không được gán cho nhân viên: REVIEW.GRAY_ZONE, parent.story.author, x.y.", ex.Message);
    }

    [Fact]
    public async Task Duplicate_codes_are_merged()
    {
        var codes = StaffDefaults(RoleCodes.Moderator);
        await Service().UpdateRolePermissionsAsync(AdminUserId, RoleCodes.Moderator, Request([.. codes, .. codes]));
        _db.ChangeTracker.Clear();
        Assert.Equal(DefaultRolePermissions.Map[RoleCodes.Moderator].Order(StringComparer.Ordinal), await CodesOf(RoleCodes.Moderator));
    }

    [Fact]
    public async Task Administrator_cannot_drop_permission_manage()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(() => Service().UpdateRolePermissionsAsync(
            AdminUserId, RoleCodes.Administrator,
            Request(StaffDefaults(RoleCodes.Administrator).Where(c => c != Permissions.Admin.PermissionManage).ToArray())));
        Assert.Equal("PERMISSION_MANAGE_REQUIRED", ex.ErrorCode);
    }

    [Fact]
    public async Task Last_reviewer_of_a_step_cannot_be_removed()
    {
        await Service().UpdateRolePermissionsAsync(AdminUserId, RoleCodes.Administrator,
            Request(StaffDefaults(RoleCodes.Administrator).Where(c => c != Permissions.Review.GrayZone).ToArray()));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Service().UpdateRolePermissionsAsync(
            AdminUserId, RoleCodes.Moderator,
            Request(StaffDefaults(RoleCodes.Moderator).Where(c => c != Permissions.Review.GrayZone).ToArray())));
        Assert.Equal("REVIEWER_REQUIRED", ex.ErrorCode);
        Assert.Equal("Phải còn ít nhất một role duyệt bước này: review.gray_zone.", ex.Message);
    }

    [Fact]
    public async Task Moderator_only_review_step_cannot_be_removed_from_moderator()
    {
        // moderation.published.review mặc định chỉ Moderator giữ.
        var ex = await Assert.ThrowsAsync<ConflictException>(() => Service().UpdateRolePermissionsAsync(
            AdminUserId, RoleCodes.Moderator, Request()));
        Assert.Equal("REVIEWER_REQUIRED", ex.ErrorCode);
    }

    [Fact]
    public async Task Moderator_can_drop_shared_review_steps_while_admin_keeps_them()
    {
        var keep = new[] { Permissions.Moderation.PublishedReview };
        await Service().UpdateRolePermissionsAsync(AdminUserId, RoleCodes.Moderator, Request(keep));
        _db.ChangeTracker.Clear();
        Assert.DoesNotContain(Permissions.Review.GrayZone, await CodesOf(RoleCodes.Moderator));
    }

    [Fact]
    public async Task Failed_update_leaves_permissions_unchanged()
    {
        var before = await CodesOf(RoleCodes.Administrator);
        await Assert.ThrowsAsync<ConflictException>(() => Service().UpdateRolePermissionsAsync(
            AdminUserId, RoleCodes.Administrator, Request()));
        _db.ChangeTracker.Clear();
        Assert.Equal(before, await CodesOf(RoleCodes.Administrator));
        Assert.Empty(await _db.Set<AuditLog>().ToListAsync());
    }

    private sealed class RecordingPermissionService : IPermissionService
    {
        public List<string> Invalidated { get; } = [];
        public Task<bool> RoleHasAsync(string roleCode, string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlySet<string>> GetForRoleAsync(string roleCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task InvalidateAsync(string roleCode, CancellationToken cancellationToken = default)
        {
            Invalidated.Add(roleCode);
            return Task.CompletedTask;
        }
    }
}
