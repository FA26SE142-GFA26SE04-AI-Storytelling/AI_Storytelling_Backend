using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Configurations;

namespace StoryPlatform.UnitTests.Features.AccessControl;

/// <summary>SQLite in-memory chỉ gồm bảng phân quyền và audit_logs, dùng cấu hình EF thật của các bảng đó.</summary>
internal sealed class AccessControlTestDb(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        Type[] keep = [typeof(Role), typeof(Permission), typeof(RolePermission), typeof(AuditLog)];
        foreach (var type in b.Model.GetEntityTypes().Select(x => x.ClrType).ToArray())
            if (!keep.Contains(type)) b.Ignore(type);
        new RoleConfiguration().Configure(b.Entity<Role>());
        new PermissionConfiguration().Configure(b.Entity<Permission>());
        new RolePermissionConfiguration().Configure(b.Entity<RolePermission>());
        b.Entity<AuditLog>().HasKey(x => x.Id);
        b.Entity<AuditLog>().Ignore(x => x.ActorUser);
        b.Entity<AuditLog>().ToTable("audit_logs");
    }

    public static async Task<(SqliteConnection Connection, AccessControlTestDb Db)> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new AccessControlTestDb(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        return (connection, db);
    }
}
