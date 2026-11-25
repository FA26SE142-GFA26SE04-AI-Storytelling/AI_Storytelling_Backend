using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Features.AccessControl;
using StoryPlatform.Application.Features.AccessControl.Services;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>Frontend ẩn menu theo quyền của chính người đăng nhập; quyền bị thu phải biến mất khỏi danh sách.</summary>
public sealed class MyPermissionsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Url = "/api/v1/Auth/me/permissions";

    private static async Task<List<string>> ReadAsync(HttpClient client)
    {
        var response = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data")
            .EnumerateArray().Select(x => x.GetString()!).ToList();
    }

    [Fact]
    public async Task Anonymous_is_unauthorized() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(Url)).StatusCode);

    [Fact]
    public async Task Returns_only_the_callers_role_permissions_and_drops_revoked_ones()
    {
        var parent = await ReadAsync(await factory.ClientForNewUserAsync(UserRole.User));
        Assert.Equal(DefaultRolePermissions.Map[RoleCodes.User].Order(StringComparer.Ordinal), parent);

        var moderator = await factory.ClientForNewUserAsync(UserRole.Moderator);
        Assert.Contains(Permissions.Moderation.PublishedView, await ReadAsync(moderator));

        using (var scope = factory.Services.CreateScope())
            await new PermissionCatalogSeeder(scope.ServiceProvider.GetRequiredService<IUnitOfWork>()).SeedAsync(CancellationToken.None);
        var kept = DefaultRolePermissions.Map[RoleCodes.Moderator].Where(c => c != Permissions.Moderation.PublishedView).ToList();
        var admin = await factory.ClientForNewUserAsync(UserRole.Administrator);
        var saved = await admin.PutAsJsonAsync("/api/v1/admin/roles/Moderator/permissions", new { permissionCodes = kept });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        Assert.DoesNotContain(Permissions.Moderation.PublishedView, await ReadAsync(moderator));
    }
}
