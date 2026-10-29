using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoryPlatform.Application.Features.AuditLogs.Services;
using StoryPlatform.Application.Features.AuthoringGuideManagement.Services;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.IntegrationTests.Postgres;

/// <summary>
/// Tạo một database "template" đã áp dụng toàn bộ migration và đã seed hướng dẫn mặc định, rồi nhân bản nó cho từng test.
/// Không chạm bất kỳ database nào khác ngoài các database tạm do chính fixture tạo ra.
/// </summary>
public sealed class AuthoringGuidePostgresFixture : IAsyncLifetime
{
    private string? _adminConnection;
    private string? _templateName;
    private bool _templateCreated;
    private int _moderatorId, _administratorId, _parentId;

    public async Task InitializeAsync()
    {
        var configuredConnection = Environment.GetEnvironmentVariable(PostgresFactAttribute.EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredConnection)) return; // mọi test sẽ bị bỏ qua
        _adminConnection = new NpgsqlConnectionStringBuilder(configuredConnection)
        {
            Pooling = false, IncludeErrorDetail = false, LogParameters = false
        }.ConnectionString;

        _templateName = "storyplatform_it_tpl_" + Guid.NewGuid().ToString("N")[..12];
        await ExecuteAdminAsync($"CREATE DATABASE \"{_templateName}\"");
        _templateCreated = true;

        try
        {
            await using var context = new ApplicationDbContext(OptionsFor(_templateName));
            await context.Database.MigrateAsync();
            var moderator = NewUser("it_moderator", UserRole.Moderator);
            var administrator = NewUser("it_administrator", UserRole.Administrator);
            var parent = NewUser("it_parent", UserRole.User);
            context.Users.AddRange(moderator, administrator, parent);
            await context.SaveChangesAsync();
            (_moderatorId, _administratorId, _parentId) = (moderator.Id, administrator.Id, parent.Id);

            await using var uow = new UnitOfWork(context);
            await new AuthoringGuideCatalogSeeder(uow).SeedAsync(CancellationToken.None);
        }
        catch
        {
            await DisposeAsync(); // dọn template ngay cả khi migrate/seed thất bại
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (!_templateCreated) return;
        await ExecuteAdminAsync($"DROP DATABASE IF EXISTS \"{_templateName}\" WITH (FORCE)");
        _templateCreated = false;
    }

    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        if (!_templateCreated)
            throw new InvalidOperationException("PostgreSQL fixture chưa được bật và khởi tạo.");
        var name = "storyplatform_it_" + Guid.NewGuid().ToString("N")[..12];
        await ExecuteAdminAsync($"CREATE DATABASE \"{name}\" TEMPLATE \"{_templateName}\"");
        return new TestDatabase(name, OptionsFor(name), _moderatorId, _administratorId, _parentId, ExecuteAdminAsync);
    }

    private DbContextOptions<ApplicationDbContext> OptionsFor(string database)
    {
        // Không pooling: đóng hết kết nối trước khi clone/drop, không ảnh hưởng pool của test khác.
        var builder = new NpgsqlConnectionStringBuilder(_adminConnection) { Database = database, Pooling = false };
        return new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(builder.ConnectionString).Options;
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static UserAccount NewUser(string username, UserRole role) => new()
    {
        Username = username, Email = username + "@example.test", PasswordHash = "not-a-real-hash", FullName = username,
        Role = role, Status = AccountStatus.Active
    };
}

public sealed class TestDatabase(string name, DbContextOptions<ApplicationDbContext> options, int moderatorId, int administratorId,
    int parentUserId, Func<string, Task> executeAdmin) : IAsyncDisposable
{
    public DbContextOptions<ApplicationDbContext> Options { get; } = options;
    public int ModeratorId { get; } = moderatorId;
    public int AdministratorId { get; } = administratorId;
    public int ParentUserId { get; } = parentUserId;

    /// <summary>Mỗi scope là một "request" riêng: DbContext + UnitOfWork + service riêng, giống container DI thật.</summary>
    public Scope NewScope() => new(Options);

    public async ValueTask DisposeAsync()
    {
        await executeAdmin($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
    }
}

public sealed class Scope : IAsyncDisposable
{
    private readonly UnitOfWork _uow;

    public Scope(DbContextOptions<ApplicationDbContext> options)
    {
        Context = new ApplicationDbContext(options);
        _uow = new UnitOfWork(Context);
        Management = new AuthoringGuideManagementService(_uow, new AuditLogWriter(_uow));
        Parent = new AuthoringGuideService(_uow);
    }

    public ApplicationDbContext Context { get; }
    public AuthoringGuideManagementService Management { get; }
    public AuthoringGuideService Parent { get; }

    public ValueTask DisposeAsync() => _uow.DisposeAsync();
}
