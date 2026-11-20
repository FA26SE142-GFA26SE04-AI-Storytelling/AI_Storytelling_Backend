using System.Net;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>
/// Màn audit log của Administrator qua HTTP thật (JWT, phân quyền, EF trên SQLite): bộ lọc phải dịch được sang SQL.
/// </summary>
public sealed class AdminAuditLogApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Admin_FiltersAuditLogs_AndReadsDetailAndFilterValues()
    {
        var admin = await factory.AddUserAsync(UserRole.Administrator);
        var adminClient = factory.ClientFor(admin);
        var marker = $"AuditViewerTest{Guid.NewGuid():N}"[..30];
        var at = new DateTime(2026, 11, 10, 3, 0, 0, DateTimeKind.Utc);
        var ids = await factory.WithDbAsync(async db =>
        {
            var byAdmin = new AuditLog
            {
                ActorUserId = admin.Id, Action = marker, EntityType = "PlanSettings", EntityId = 1, OccurredAt = at,
                BeforeState = "{\"limit\":3}", AfterState = "{\"limit\":5}"
            };
            var bySystem = new AuditLog
            {
                ActorUserId = null, Action = marker, EntityType = "UserAccount", EntityId = 42, OccurredAt = at.AddHours(2)
            };
            db.AuditLogs.AddRange(byAdmin, bySystem);
            await db.SaveChangesAsync();
            return (byAdmin.Id, bySystem.Id);
        });

        var all = await AdminApi.GetDataAsync(adminClient, $"/api/v1/admin/audit-logs?action={marker}");
        Assert.Equal(2, all.GetProperty("totalCount").GetInt32());
        var items = all.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([ids.Item2, ids.Item1], items.Select(item => item.GetProperty("id").GetInt32()));
        Assert.Equal(admin.Username, items[1].GetProperty("actorUsername").GetString());

        var system = await AdminApi.GetDataAsync(adminClient, $"/api/v1/admin/audit-logs?action={marker}&systemOnly=true");
        Assert.Equal(ids.Item2, Assert.Single(system.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt32());

        var filtered = await AdminApi.GetDataAsync(adminClient,
            $"/api/v1/admin/audit-logs?action={marker}&actorUserId={admin.Id}&entityType=PlanSettings&entityId=1" +
            "&from=2026-11-10T00:00:00Z&to=2026-11-10T03:00:00Z&pageSize=5");
        Assert.Equal(ids.Item1, Assert.Single(filtered.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt32());

        var detail = await AdminApi.GetDataAsync(adminClient, $"/api/v1/admin/audit-logs/{ids.Item1}");
        Assert.Equal(("{\"limit\":3}", "{\"limit\":5}"),
            (detail.GetProperty("beforeState").GetString(), detail.GetProperty("afterState").GetString()));

        var filters = await AdminApi.GetDataAsync(adminClient, "/api/v1/admin/audit-logs/filters");
        Assert.Contains(filters.GetProperty("actions").EnumerateArray(), action => action.GetString() == marker);
        Assert.Contains(filters.GetProperty("entityTypes").EnumerateArray(), type => type.GetString() == "PlanSettings");

        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.GetAsync("/api/v1/admin/audit-logs/99999999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.GetAsync(
            "/api/v1/admin/audit-logs?from=2026-11-10T00:00:00Z&to=2026-11-09T00:00:00Z")).StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Moderator)]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.ContentCreator)]
    public async Task NonAdministrators_AreForbidden(UserRole role)
    {
        var client = await factory.ClientForNewUserAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/audit-logs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/audit-logs/filters")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/admin/audit-logs")).StatusCode);
    }
}
