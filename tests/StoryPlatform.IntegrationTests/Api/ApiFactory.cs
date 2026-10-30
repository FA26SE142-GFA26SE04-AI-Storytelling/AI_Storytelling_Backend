using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>
/// Hosts the real API pipeline (routing, JWT, authorization, exception middleware) over an in-memory
/// SQLite database. Background workers are removed and outbound AI/email/storage clients are faked.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string WebhookApiKey = "test-sepay-webhook-key";
    public const string Password = "Test@12345";
    public const string AllowedOrigin = "https://www.taletale.site";
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public ApiFactory()
    {
        _connection.Open();
        // UnitOfWork serializes workflows with a PostgreSQL advisory lock; SQLite has a single writer anyway.
        _connection.CreateFunction("pg_advisory_xact_lock", (long _) => 0L);
    }

    public RecordingEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // UseSetting is visible to Program.cs before Build(); test-only values, not real secrets.
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=unused;Database=unused");
        builder.UseSetting("Database:RunMigrationsOnStartup", "false");
        builder.UseSetting("SeedData:RunOnStartup", "false");
        builder.UseSetting("AuthoringGuides:SeedOnStartup", "false");
        builder.UseSetting("JwtSettings:SecretKey", "integration-test-signing-key-0123456789abcdef");
        builder.UseSetting("JwtSettings:Issuer", "StoryPlatform");
        builder.UseSetting("JwtSettings:Audience", "StoryPlatformClient");
        builder.UseSetting("JwtSettings:ExpiryMinutes", "15");
        builder.UseSetting("JwtSettings:RefreshTokenExpiryDays", "7");
        builder.UseSetting("JwtSettings:ChildTokenExpiryMinutes", "240");
        builder.UseSetting("SePaySettings:WebhookApiKey", WebhookApiKey);
        builder.UseSetting("Cors:AllowedOrigins:0", AllowedOrigin);
        // Functional tests share one client IP; RateLimitedApiFactory lowers these to test throttling.
        builder.UseSetting("RateLimiting:Auth:PermitLimit", "10000");
        builder.UseSetting("RateLimiting:Ai:PermitLimit", "10000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll(descriptor => descriptor.ServiceType == typeof(IHostedService));
            services.RemoveAll(descriptor =>
                descriptor.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                descriptor.ServiceType == typeof(DbContextOptions));
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll(descriptor => descriptor.ServiceType == typeof(IDistributedCache));
            services.AddDistributedMemoryCache();
            services.RemoveAll(descriptor => descriptor.ServiceType == typeof(IEmailSender));
            services.AddSingleton<IEmailSender>(Emails);
            services.RemoveAll(descriptor => descriptor.ServiceType == typeof(IAIStoryGenerationClient));
            services.AddSingleton<IAIStoryGenerationClient, UnavailableAIStoryGenerationClient>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
        return host;
    }

    public async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var result = await action(db);
        await db.SaveChangesAsync();
        return result;
    }

    public Task WithDbAsync(Func<ApplicationDbContext, Task> action) =>
        WithDbAsync(async db => { await action(db); return true; });

    /// <summary>Adds an active account that can log in with <see cref="Password"/>.</summary>
    public async Task<UserAccount> AddUserAsync(UserRole role, string? username = null,
        AccountStatus status = AccountStatus.Active)
    {
        var name = username ?? $"{role.ToString().ToLowerInvariant()}_{Guid.NewGuid():N}"[..20];
        using var scope = Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = new UserAccount
        {
            Username = name,
            Email = $"{name}@example.test",
            FullName = name,
            PasswordHash = hasher.HashPassword(Password),
            Role = role,
            Status = status
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public HttpClient ClientFor(UserAccount user)
    {
        using var scope = Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>().GenerateAccessToken(user);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<HttpClient> ClientForNewUserAsync(UserRole role) => ClientFor(await AddUserAsync(role));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}

internal static class ServiceCollectionTestExtensions
{
    public static void RemoveAll(this IServiceCollection services, Func<ServiceDescriptor, bool> predicate)
    {
        foreach (var descriptor in services.Where(predicate).ToArray()) services.Remove(descriptor);
    }
}

internal static class HttpTestExtensions
{
    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body);
}
