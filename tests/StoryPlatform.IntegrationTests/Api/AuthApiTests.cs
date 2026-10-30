using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

public sealed class AuthApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTokens()
    {
        var user = await factory.AddUserAsync(UserRole.User);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/login",
            new { identifier = user.Username, password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await ReadJsonAsync(response)).GetProperty("data");
        Assert.False(string.IsNullOrEmpty(data.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrEmpty(data.GetProperty("refreshToken").GetString()));
        Assert.Equal("User", data.GetProperty("user").GetProperty("role").GetString());
    }

    [Fact]
    public async Task Login_WrongPassword_Returns400()
    {
        var user = await factory.AddUserAsync(UserRole.User);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/login",
            new { identifier = user.Username, password = "Wrong@12345" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Tên đăng nhập hoặc mật khẩu không chính xác.", (await ReadJsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Login_MissingPassword_Returns400ValidationError()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/login", new { identifier = "someone" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Mật khẩu không được để trống.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_UnverifiedAccount_Returns403()
    {
        var user = await factory.AddUserAsync(UserRole.User, status: AccountStatus.Registered);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/login",
            new { identifier = user.Username, password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RefreshToken_ValidToken_Returns200WithRotatedToken()
    {
        var user = await factory.AddUserAsync(UserRole.User);
        var client = factory.CreateClient();
        var login = (await ReadJsonAsync(await client.PostAsJsonAsync("/api/v1/Auth/login",
            new { identifier = user.Username, password = ApiFactory.Password }))).GetProperty("data");
        var refreshToken = login.GetProperty("refreshToken").GetString();

        var response = await client.PostAsJsonAsync("/api/v1/Auth/refresh-token", new { refreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(refreshToken, (await ReadJsonAsync(response)).GetProperty("data").GetProperty("refreshToken").GetString());
    }

    [Fact]
    public async Task RefreshToken_UnknownToken_Returns401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/refresh-token", new { refreshToken = "not-a-real-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RefreshToken_EmptyBody_Returns400()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/refresh-token", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_ValidRequest_Returns201AndSendsVerificationOtp()
    {
        var name = $"reg_{Guid.NewGuid():N}"[..16];

        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/register", new
        {
            username = name, email = $"{name}@example.test", fullName = "Phụ huynh Test",
            password = "Parent@12345", confirmPassword = "Parent@12345"
        });

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created, $"Status was {response.StatusCode}");
        Assert.Contains(factory.Emails.Sent, mail => mail.To == $"{name}@example.test" && mail.Kind == "verify");
    }

    [Fact]
    public async Task Register_PasswordConfirmationMismatch_Returns400()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/register", new
        {
            username = "mismatch_user", email = "mismatch@example.test", fullName = "Test",
            password = "Parent@12345", confirmPassword = "Parent@99999"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Mật khẩu xác nhận không khớp.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns400()
    {
        var existing = await factory.AddUserAsync(UserRole.User);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/register", new
        {
            username = $"dup_{Guid.NewGuid():N}"[..12], email = existing.Email, fullName = "Test",
            password = "Parent@12345", confirmPassword = "Parent@12345"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/Auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_TokenOfSuspendedAccount_Returns401()
    {
        var user = await factory.AddUserAsync(UserRole.User);
        var client = factory.ClientFor(user);
        await factory.WithDbAsync(async db => (await db.Users.FindAsync(user.Id))!.Status = AccountStatus.Suspended);

        var response = await client.GetAsync("/api/v1/Auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    internal static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
