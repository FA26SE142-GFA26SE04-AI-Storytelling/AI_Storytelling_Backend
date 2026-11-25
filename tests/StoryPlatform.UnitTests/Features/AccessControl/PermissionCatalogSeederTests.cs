using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoryPlatform.Application.Features.AccessControl;
using StoryPlatform.Application.Features.AccessControl.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.UnitTests.Features.AccessControl;

public sealed class PermissionCatalogSeederTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ApplicationDbContext _db = null!;

    public async Task InitializeAsync() => (_connection, _db) = await AccessControlTestDb.CreateAsync();

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<int> SeedAsync()
    {
        var uow = new UnitOfWork(_db); // không dispose: UnitOfWork dispose luôn DbContext dùng chung của test
        var added = await new PermissionCatalogSeeder(uow).SeedAsync(CancellationToken.None);
        _db.ChangeTracker.Clear();
        return added;
    }

    private Task<List<string>> CodesOf(string role) => _db.Set<RolePermission>().AsNoTracking()
        .Where(x => x.Role.Code == role).Select(x => x.Permission.Code).ToListAsync();

    [Fact]
    public async Task First_run_creates_roles_permissions_and_defaults()
    {
        Assert.True(await SeedAsync() > 0);

        var roles = await _db.Set<Role>().AsNoTracking().ToListAsync();
        Assert.Equal(RoleCodes.All.OrderBy(x => x), roles.Select(r => r.Code).OrderBy(x => x));
        Assert.Equal(RoleCodes.Staff.OrderBy(x => x), roles.Where(r => r.IsStaff).Select(r => r.Code).OrderBy(x => x));
        Assert.Equal(PermissionCatalog.All.Count, await _db.Set<Permission>().CountAsync());
        Assert.Equal(DefaultRolePermissions.Map[RoleCodes.Moderator].OrderBy(x => x), (await CodesOf(RoleCodes.Moderator)).OrderBy(x => x));
    }

    [Fact]
    public async Task Second_run_adds_nothing()
    {
        await SeedAsync();
        Assert.Equal(0, await SeedAsync());
    }

    [Fact]
    public async Task Edited_role_permissions_are_kept()
    {
        await SeedAsync();
        var link = await _db.Set<RolePermission>()
            .SingleAsync(x => x.Role.Code == RoleCodes.Moderator && x.Permission.Code == Permissions.Review.GrayZone);
        _db.Remove(link);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await SeedAsync();

        Assert.DoesNotContain(Permissions.Review.GrayZone, await CodesOf(RoleCodes.Moderator));
    }

    [Fact]
    public async Task Permission_new_to_the_database_is_granted_to_roles_that_already_have_permissions()
    {
        // DB đã phân quyền trước khi có permission sự kiện: giả lập bằng cách xóa permission đó sau lần seed đầu.
        await SeedAsync();
        _db.RemoveRange(await _db.Set<RolePermission>().Where(x => x.Permission.Code == Permissions.Moderation.EventManage).ToListAsync());
        _db.Remove(await _db.Set<Permission>().SingleAsync(p => p.Code == Permissions.Moderation.EventManage));
        var gray = await _db.Set<RolePermission>()
            .SingleAsync(x => x.Role.Code == RoleCodes.Moderator && x.Permission.Code == Permissions.Review.GrayZone);
        _db.Remove(gray);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Assert.Equal(2, await SeedAsync()); // 1 permission + 1 liên kết cho Moderator

        var moderator = await CodesOf(RoleCodes.Moderator);
        Assert.Contains(Permissions.Moderation.EventManage, moderator);
        Assert.DoesNotContain(Permissions.Review.GrayZone, moderator); // quyền Administrator đã gỡ vẫn giữ nguyên
        Assert.DoesNotContain(Permissions.Moderation.EventManage, await CodesOf(RoleCodes.Administrator));
        Assert.Equal(0, await SeedAsync());
    }

    [Fact]
    public async Task Code_removed_from_catalog_is_soft_deleted_and_metadata_refreshed()
    {
        await SeedAsync();
        _db.Add(new Permission { Code = "old.code", Group = PermissionCatalog.AdminGroup, Description = "Cũ", IsStaffGrantable = true });
        var gray = await _db.Set<Permission>().SingleAsync(p => p.Code == Permissions.Review.GrayZone);
        gray.Description = "sai";
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await SeedAsync();

        var all = await _db.Set<Permission>().IgnoreQueryFilters().AsNoTracking().ToListAsync();
        Assert.True(all.Single(p => p.Code == "old.code").IsDeleted);
        Assert.Equal(PermissionCatalog.Find(Permissions.Review.GrayZone)!.Description,
            all.Single(p => p.Code == Permissions.Review.GrayZone).Description);
    }
}
