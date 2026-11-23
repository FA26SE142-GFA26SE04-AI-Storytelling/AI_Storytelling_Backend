using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoryPlatform.Application.Abstractions.Caching;
using StoryPlatform.Application.Features.AccessControl;
using StoryPlatform.Application.Features.AccessControl.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.UnitTests.Features.AccessControl;

public sealed class PermissionServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ApplicationDbContext _db = null!;

    public async Task InitializeAsync() => (_connection, _db) = await AccessControlTestDb.CreateAsync();

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task SeedAsync()
    {
        await new PermissionCatalogSeeder(new UnitOfWork(_db)).SeedAsync(CancellationToken.None);
        _db.ChangeTracker.Clear();
    }

    private PermissionService Service(IAppCache? cache = null) => new(new UnitOfWork(_db), cache);

    [Fact]
    public async Task Reads_permissions_from_database()
    {
        await SeedAsync();
        var link = await _db.Set<RolePermission>()
            .SingleAsync(x => x.Role.Code == RoleCodes.Moderator && x.Permission.Code == Permissions.Review.GrayZone);
        _db.Remove(link);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Assert.False(await Service().RoleHasAsync(RoleCodes.Moderator, Permissions.Review.GrayZone));
        Assert.True(await Service().RoleHasAsync(RoleCodes.Administrator, Permissions.Review.GrayZone));
    }

    [Fact]
    public async Task Falls_back_to_defaults_when_role_row_missing()
    {
        Assert.True(await Service().RoleHasAsync(RoleCodes.Moderator, Permissions.Review.GrayZone));
        Assert.False(await Service().RoleHasAsync(RoleCodes.User, Permissions.Review.GrayZone));
    }

    [Fact]
    public async Task Role_with_no_links_has_no_permissions()
    {
        await SeedAsync();
        _db.RemoveRange(_db.Set<RolePermission>().Where(x => x.Role.Code == RoleCodes.Moderator));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Assert.Empty(await Service().GetForRoleAsync(RoleCodes.Moderator));
    }

    [Fact]
    public async Task Soft_deleted_permission_is_not_granted()
    {
        await SeedAsync();
        var gray = await _db.Set<Permission>().SingleAsync(p => p.Code == Permissions.Review.GrayZone);
        gray.IsDeleted = true;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Assert.False(await Service().RoleHasAsync(RoleCodes.Moderator, Permissions.Review.GrayZone));
    }

    [Fact]
    public async Task Uses_cache_and_invalidate_removes_role_key()
    {
        await SeedAsync();
        var cache = new RecordingCache();
        var service = Service(cache);

        Assert.True(await service.RoleHasAsync(RoleCodes.Moderator, Permissions.Review.GrayZone));
        Assert.Contains("access:role-permissions:Moderator", cache.Reads);

        await service.InvalidateAsync(RoleCodes.Moderator);
        Assert.Contains("access:role-permissions:Moderator", cache.Removed);
    }

    [Fact]
    public async Task Works_without_cache()
    {
        await SeedAsync();
        Assert.True(await Service(null).RoleHasAsync(RoleCodes.Administrator, Permissions.Admin.PermissionManage));
    }

    [Fact]
    public void Cached_value_round_trips_through_json_like_redis_cache()
    {
        var value = new PermissionService.CachedRolePermissions(true, [Permissions.Review.GrayZone]);
        var copy = System.Text.Json.JsonSerializer.Deserialize<PermissionService.CachedRolePermissions>(
            System.Text.Json.JsonSerializer.Serialize(value))!;
        Assert.True(copy.RoleFound);
        Assert.Equal(value.Codes, copy.Codes);
    }

    [Fact]
    public void Runtime_registration_uses_database_service()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        StoryPlatform.Api.Extensions.ServiceExtensions.AddAppAuthorization(services);
        StoryPlatform.Application.DependencyInjection.AddApplication(services);
        var last = services.Last(d => d.ServiceType == typeof(StoryPlatform.Application.Features.AccessControl.Interfaces.IPermissionService));
        Assert.Equal(typeof(PermissionService), last.ImplementationType);
    }

    private sealed class RecordingCache : IAppCache
    {
        public List<string> Reads { get; } = [];
        public List<string> Removed { get; } = [];

        public Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl,
            CancellationToken cancellationToken = default)
        {
            Reads.Add(key);
            return factory(cancellationToken);
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            Removed.Add(key);
            return Task.CompletedTask;
        }
    }
}
