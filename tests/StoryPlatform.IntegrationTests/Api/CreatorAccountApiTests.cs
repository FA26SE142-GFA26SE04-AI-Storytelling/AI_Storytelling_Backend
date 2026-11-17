using System.Net;
using System.Net.Http.Json;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>Luồng 5, M5 qua HTTP thật: Admin vô hiệu hoá Creator thì token đang dùng bị thu hồi, kích hoạt lại thì đăng nhập được.</summary>
public sealed class CreatorAccountApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task DeactivatedCreator_LosesAccessImmediately_AndCanLogInAgainAfterReactivation()
    {
        var account = await factory.AddUserAsync(UserRole.ContentCreator);
        var creator = factory.ClientFor(account);
        Assert.Equal(HttpStatusCode.OK, (await creator.GetAsync("/api/v1/TokenQuota/creator/me")).StatusCode);
        var admin = await factory.ClientForNewUserAsync(UserRole.Administrator);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(
            $"/api/v1/admin/creators/{account.Id}/deactivate", new { reason = "" })).StatusCode);
        var result = await AdminApi.PostDataAsync(admin, $"/api/v1/admin/creators/{account.Id}/deactivate",
            new { reason = "Đăng nội dung sai độ tuổi nhiều lần" }, HttpStatusCode.OK);
        Assert.Equal("Suspended", result.GetProperty("creator").GetProperty("status").GetString());

        // Token đang dùng bị thu hồi ngay; đăng nhập lại bị chặn.
        Assert.Equal(HttpStatusCode.Unauthorized, (await creator.GetAsync("/api/v1/TokenQuota/creator/me")).StatusCode);
        var login = new { identifier = account.Username, password = ApiFactory.Password };
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/login", login)).StatusCode);

        var suspended = await AdminApi.GetDataAsync(admin, "/api/v1/admin/creators?status=Suspended");
        Assert.Contains(suspended.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetInt32() == account.Id);

        await AdminApi.PostDataAsync(admin, $"/api/v1/admin/creators/{account.Id}/reactivate", new { }, HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/login", login)).StatusCode);
    }

    [Fact]
    public async Task OnlyAdministrators_ManageCreators()
    {
        var account = await factory.AddUserAsync(UserRole.ContentCreator);
        foreach (var role in new[] { UserRole.Moderator, UserRole.ContentCreator, UserRole.User })
        {
            var client = await factory.ClientForNewUserAsync(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/creators")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
                $"/api/v1/admin/creators/{account.Id}/deactivate", new { reason = "x" })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/admin/creators")).StatusCode);
    }
}
