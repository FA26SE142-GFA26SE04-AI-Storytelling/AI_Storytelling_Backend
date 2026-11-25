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

    private static string[] Defaults(string role) => DefaultRolePermissions.Map[role].ToArray();

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
        var target = Defaults(RoleCodes.Moderator).Where(c => c != Permissions.Moderation.CategoryManage).ToArray();

        var result = await Service().UpdateRolePermissionsAsync(AdminUserId, RoleCodes.Moderator, Request(target));
        _db.ChangeTracker.Clear();

        var expected = target.Order(StringComparer.Ordinal).ToList();
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
    public async Task Granting_a_permission_outside_the_role_defaults_is_rejected()
    {
        // Service của chi trả vẫn đòi Administrator, nên cấp cho Moderator sẽ không dùng được (chặn đến giai đoạn 3).
        var ex = await Assert.ThrowsAsync<BadRequestException>(() => Service().UpdateRolePermissionsAsync(
            AdminUserId, RoleCodes.Moderator,
            Request([.. Defaults(RoleCodes.Moderator), Permissions.Admin.CreatorPayoutManage, Permissions.Admin.AuditView])));
        Assert.Equal("Chưa hỗ trợ cấp các quyền này cho Moderator: audit.view, creator.payout.manage.", ex.Message);
        _db.ChangeTracker.Clear();
        Assert.DoesNotContain(Permissions.Admin.CreatorPayoutManage, await CodesOf(RoleCodes.Moderator));
    }

    [Fact]
    public async Task Revoked_default_can_be_restored()
    {
        var without = Defaults(RoleCodes.Moderator).Where(c => c != Permissions.Review.GrayZone).ToArray();
        await Service().UpdateRolePermissionsAsync(AdminUserId, RoleCodes.Moderator, Request(without));
        _db.ChangeTracker.Clear(); // mỗi request có DbContext riêng
        await Service().UpdateRolePermissionsAsync(AdminUserId, RoleCodes.Moderator, Request(Defaults(RoleCodes.Moderator)));
        _db.ChangeTracker.Clear();
        Assert.Contains(Permissions.Review.GrayZone, await CodesOf(RoleCodes.Moderator));
    }

    [Fact]
    public async Task Previously_granted_extra_permission_can_be_kept_or_removed()
    {
        var moderator = await _db.Set<Role>().SingleAsync(r => r.Code == RoleCodes.Moderator);
        var audit = await _db.Set<Permission>().SingleAsync(p => p.Code == Permissions.Admin.AuditView);
        _db.Add(new RolePermission { RoleId = moderator.Id, PermissionId = audit.Id });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await Service().UpdateRolePermissionsAsync(AdminUserId, RoleCodes.Moderator,
            Request([.. Defaults(RoleCodes.Moderator), Permissions.Admin.AuditView]));
        _db.ChangeTracker.Clear();
        Assert.Contains(Permissions.Admin.AuditView, await CodesOf(RoleCodes.Moderator));

        await Service().UpdateRolePermissionsAsync(AdminUserId, RoleCodes.Moderator, Request(Defaults(RoleCodes.Moderator)));
        _db.ChangeTracker.Clear();
        Assert.DoesNotContain(Permissions.Admin.AuditView, await CodesOf(RoleCodes.Moderator));
    }

    [Fact]
    public async Task Permissions_report_scope_and_role_permissions_report_conditions()
    {
        var permissions = await Service().ListPermissionsAsync();
        Assert.Equal("Assigned", permissions.Single(p => p.Code == Permissions.Review.GrayZone).Scope);
        Assert.Equal("Any", permissions.Single(p => p.Code == Permissions.Review.GuideManage).Scope);

        var admin = await Service().GetRolePermissionsAsync(RoleCodes.Administrator);
        Assert.Equal(
            [AccessConditions.Describe(AccessConditions.NotOwnContent), AccessConditions.Describe(AccessConditions.AdminFallback)],
            admin.Conditions[Permissions.Moderation.QueueProcess]);
        Assert.Equal([AccessConditions.Describe(AccessConditions.NoActiveModerator)], admin.Conditions[Permissions.Review.GuideManage]);
        Assert.False(admin.Conditions.ContainsKey(Permissions.Admin.AuditView));

        var moderator = await Service().GetRolePermissionsAsync(RoleCodes.Moderator);
        Assert.False(moderator.Conditions.ContainsKey(Permissions.Review.GuideManage));
    }

    [Fact]
    public async Task Parent_and_creator_permissions_report_ownership_and_plan_rules()
    {
        var parent = await Service().GetRolePermissionsAsync(RoleCodes.User);
        Assert.Contains(AccessConditions.Describe(AccessConditions.OwnChildProfile), parent.Conditions[Permissions.Parent.ChildProfileManage]);
        Assert.Contains(AccessConditions.Describe(AccessConditions.ParentalGate), parent.Conditions[Permissions.Parent.ChildProfileManage]);
        Assert.Contains(AccessConditions.Describe(AccessConditions.PlanLimits), parent.Conditions[Permissions.Parent.StoryAuthor]);
        Assert.Equal([AccessConditions.Describe(AccessConditions.DelegatedApproval)], parent.Conditions[Permissions.Shared.StoryReviewApprove]);

        var creator = await Service().GetRolePermissionsAsync(RoleCodes.ContentCreator);
        Assert.Contains(AccessConditions.Describe(AccessConditions.OwnCreatorStory), creator.Conditions[Permissions.Creator.Studio]);

        // Administrator giữ story.review.approve nhưng không phải qua ủy quyền của phụ huynh.
        var admin = await Service().GetRolePermissionsAsync(RoleCodes.Administrator);
        Assert.False(admin.Conditions.ContainsKey(Permissions.Shared.StoryReviewApprove));
    }

    [Fact]
    public async Task Role_permissions_report_defaults_role_scopes_and_condition_codes()
    {
        var roles = await Service().ListRolesAsync();
        Assert.All(roles, r => Assert.True(r.Id > 0));

        var parent = await Service().GetRolePermissionsAsync(RoleCodes.User);
        Assert.Equal(Defaults(RoleCodes.User).Order(StringComparer.Ordinal), parent.DefaultCodes);
        // Phạm vi theo role: quyền của phụ huynh chỉ trên hồ sơ của mình.
        Assert.Equal("Own", parent.Scopes[Permissions.Parent.ChildProfileManage]);
        Assert.Equal("Any", parent.Scopes[Permissions.Parent.ContentReportSubmit]);
        Assert.Equal([AccessConditions.OwnChildProfile, AccessConditions.PlanLimits, AccessConditions.ParentalGate],
            parent.ConditionCodes[Permissions.Parent.ChildProfileManage]);

        var admin = await Service().GetRolePermissionsAsync(RoleCodes.Administrator);
        Assert.Equal("Assigned", admin.Scopes[Permissions.Review.GrayZone]);
        Assert.Equal([AccessConditions.NotOwnContent, AccessConditions.AdminFallback],
            admin.ConditionCodes[Permissions.Review.GrayZone]);
        Assert.Equal(admin.AssignableCodes.Order(StringComparer.Ordinal), admin.Scopes.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Role_permissions_report_assignable_codes()
    {
        var result = await Service().GetRolePermissionsAsync(RoleCodes.Moderator);
        Assert.Equal(Defaults(RoleCodes.Moderator).Order(StringComparer.Ordinal), result.AssignableCodes);
        Assert.DoesNotContain(Permissions.Admin.CreatorPayoutManage, result.AssignableCodes);
    }

    [Theory]
    [InlineData("User", Permissions.Parent.StoryRatingWrite)]
    [InlineData("ContentCreator", Permissions.Creator.Studio)]
    [InlineData("CreatorApplicant", Permissions.Creator.ApplicationSubmit)]
    public async Task End_user_role_permission_can_be_revoked_and_restored(string role, string code)
    {
        await Service().UpdateRolePermissionsAsync(AdminUserId, role, Request(Defaults(role).Where(c => c != code).ToArray()));
        _db.ChangeTracker.Clear();
        Assert.DoesNotContain(code, await CodesOf(role));

        await Service().UpdateRolePermissionsAsync(AdminUserId, role, Request(Defaults(role)));
        _db.ChangeTracker.Clear();
        Assert.Contains(code, await CodesOf(role));
    }

    [Fact]
    public async Task Parent_cannot_be_granted_a_permission_of_another_role()
    {
        var ex = await Assert.ThrowsAsync<BadRequestException>(() => Service().UpdateRolePermissionsAsync(
            AdminUserId, RoleCodes.User, Request([.. Defaults(RoleCodes.User), Permissions.Creator.Studio])));
        Assert.Equal("Chưa hỗ trợ cấp các quyền này cho User: creator.studio.", ex.Message);
    }

    [Fact]
    public async Task Story_approval_must_keep_at_least_one_role()
    {
        // Phụ huynh và Administrator cùng giữ story.review.approve; gỡ khỏi cả hai thì không ai duyệt truyện cho trẻ.
        await Service().UpdateRolePermissionsAsync(AdminUserId, RoleCodes.User,
            Request(Defaults(RoleCodes.User).Where(c => c != Permissions.Shared.StoryReviewApprove).ToArray()));
        _db.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Service().UpdateRolePermissionsAsync(
            AdminUserId, RoleCodes.Administrator,
            Request(Defaults(RoleCodes.Administrator).Where(c => c != Permissions.Shared.StoryReviewApprove).ToArray())));
        Assert.Equal("REVIEWER_REQUIRED", ex.ErrorCode);
        Assert.Equal("Phải còn ít nhất một role duyệt bước này: story.review.approve.", ex.Message);
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
    public async Task Unknown_codes_are_rejected_with_list()
    {
        var ex = await Assert.ThrowsAsync<BadRequestException>(() => Service().UpdateRolePermissionsAsync(
            AdminUserId, RoleCodes.Moderator, Request([.. Defaults(RoleCodes.Moderator), "x.y", "REVIEW.GRAY_ZONE"])));
        Assert.Equal("Quyền không hợp lệ: REVIEW.GRAY_ZONE, x.y.", ex.Message);
    }

    [Fact]
    public async Task Duplicate_codes_are_merged()
    {
        var codes = Defaults(RoleCodes.Moderator);
        await Service().UpdateRolePermissionsAsync(AdminUserId, RoleCodes.Moderator, Request([.. codes, .. codes]));
        _db.ChangeTracker.Clear();
        Assert.Equal(DefaultRolePermissions.Map[RoleCodes.Moderator].Order(StringComparer.Ordinal), await CodesOf(RoleCodes.Moderator));
    }

    [Fact]
    public async Task Administrator_cannot_drop_permission_manage()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(() => Service().UpdateRolePermissionsAsync(
            AdminUserId, RoleCodes.Administrator,
            Request(Defaults(RoleCodes.Administrator).Where(c => c != Permissions.Admin.PermissionManage).ToArray())));
        Assert.Equal("PERMISSION_MANAGE_REQUIRED", ex.ErrorCode);
    }

    [Fact]
    public async Task Last_reviewer_of_a_step_cannot_be_removed()
    {
        await Service().UpdateRolePermissionsAsync(AdminUserId, RoleCodes.Administrator,
            Request(Defaults(RoleCodes.Administrator).Where(c => c != Permissions.Review.GrayZone).ToArray()));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Service().UpdateRolePermissionsAsync(
            AdminUserId, RoleCodes.Moderator,
            Request(Defaults(RoleCodes.Moderator).Where(c => c != Permissions.Review.GrayZone).ToArray())));
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
