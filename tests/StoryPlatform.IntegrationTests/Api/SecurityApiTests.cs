using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Domain.Enums;
using Xunit;
using static StoryPlatform.IntegrationTests.Api.AuthApiTests;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>DF06 6.1 – CORS only for configured origins.</summary>
public sealed class CorsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Preflight_FromConfiguredOrigin_IsAllowedWithoutCredentials()
    {
        var response = await factory.CreateClient().SendAsync(Preflight(ApiFactory.AllowedOrigin));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(ApiFactory.AllowedOrigin, Header(response, "Access-Control-Allow-Origin"));
        Assert.Contains("POST", Header(response, "Access-Control-Allow-Methods"));
        Assert.Null(Header(response, "Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("https://taletale.site.evil.example")]
    [InlineData("http://www.taletale.site")]
    public async Task Preflight_FromUnlistedOrigin_GetsNoAllowOriginHeader(string origin)
    {
        var response = await factory.CreateClient().SendAsync(Preflight(origin));

        Assert.Null(Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task SimpleRequest_FromConfiguredOrigin_EchoesExactOriginNotWildcard()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", ApiFactory.AllowedOrigin);

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(ApiFactory.AllowedOrigin, Header(response, "Access-Control-Allow-Origin"));
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/Auth/login");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");
        return request;
    }

    internal static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values)
        : response.Content.Headers.TryGetValues(name, out var contentValues) ? string.Join(",", contentValues) : null;
}

/// <summary>DF06 6.3 – basic security headers on every kind of response.</summary>
public sealed class SecurityHeadersApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    public static TheoryData<string, string, HttpStatusCode> Responses => new()
    {
        { "GET", "/health", HttpStatusCode.OK },
        { "GET", "/api/v1/Auth/me", HttpStatusCode.Unauthorized },
        { "GET", "/api/v1/does-not-exist", HttpStatusCode.NotFound },
        { "POST", "/api/v1/Auth/login", HttpStatusCode.BadRequest }
    };

    [Theory]
    [MemberData(nameof(Responses))]
    public async Task Response_CarriesNosniffDenyFramingAndNoReferrer(string method, string url, HttpStatusCode expected)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST") request.Content = JsonContent.Create(new { });

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("nosniff", CorsApiTests.Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", CorsApiTests.Header(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", CorsApiTests.Header(response, "Referrer-Policy"));
    }
}

/// <summary>Lowers the DF06 limits so throttling is observable in a few requests.</summary>
public sealed class RateLimitedApiFactory : ApiFactory
{
    public const int AuthLimit = 3;
    public const int AiLimit = 2;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimiting:Auth:PermitLimit", AuthLimit.ToString());
        builder.UseSetting("RateLimiting:Auth:WindowSeconds", "300");
        builder.UseSetting("RateLimiting:Ai:PermitLimit", AiLimit.ToString());
        builder.UseSetting("RateLimiting:Ai:WindowSeconds", "300");
    }
}

/// <summary>DF06 6.2 – ASP.NET Core rate limiter on auth and AI endpoints. Each test uses its own factory.</summary>
public sealed class RateLimitingApiTests
{
    [Theory]
    [InlineData("/api/v1/Auth/login", "{\"identifier\":\"nobody\",\"password\":\"Wrong@123\"}")]
    [InlineData("/api/v1/Auth/forgot-password", "{\"email\":\"nobody@example.test\"}")]
    [InlineData("/api/v1/Auth/resend-verification-email", "{\"email\":\"nobody@example.test\"}")]
    public async Task AuthEndpoint_OverLimit_Returns429WithRetryAfterAndApiMessage(string url, string body)
    {
        await using var factory = new RateLimitedApiFactory();
        var client = factory.CreateClient();

        for (var i = 0; i < RateLimitedApiFactory.AuthLimit; i++)
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Post(client, url, body)).StatusCode);
        var rejected = await Post(client, url, body);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.Equal(RateLimitingExtensions.RejectedMessage, (await ReadJsonAsync(rejected)).GetProperty("message").GetString());
        Assert.Equal("nosniff", CorsApiTests.Header(rejected, "X-Content-Type-Options"));
    }

    [Fact]
    public async Task AuthLimit_IsCountedPerEndpoint()
    {
        await using var factory = new RateLimitedApiFactory();
        var client = factory.CreateClient();
        for (var i = 0; i <= RateLimitedApiFactory.AuthLimit; i++)
            await Post(client, "/api/v1/Auth/login", "{\"identifier\":\"nobody\",\"password\":\"Wrong@123\"}");

        var forgot = await Post(client, "/api/v1/Auth/forgot-password", "{\"email\":\"nobody@example.test\"}");

        Assert.NotEqual(HttpStatusCode.TooManyRequests, forgot.StatusCode);
    }

    [Fact]
    public async Task UnthrottledAuthEndpoint_IsNotLimited()
    {
        await using var factory = new RateLimitedApiFactory();
        var client = factory.CreateClient();

        for (var i = 0; i < RateLimitedApiFactory.AuthLimit + 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await Post(client, "/api/v1/Auth/refresh-token", "{\"refreshToken\":\"unknown\"}")).StatusCode);
    }

    [Fact]
    public async Task AiEndpoint_OverLimit_Returns429ForThatAccountOnly()
    {
        await using var factory = new RateLimitedApiFactory();
        var first = await factory.ClientForNewUserAsync(UserRole.Parent);
        var second = await factory.ClientForNewUserAsync(UserRole.Parent);
        const string url = "/api/v1/ai-story-input/requests";

        for (var i = 0; i < RateLimitedApiFactory.AiLimit; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(first, url, "{}")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Post(first, url, "{}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(second, url, "{}")).StatusCode);
    }

    [Fact]
    public async Task AiEndpoint_UnauthenticatedCaller_Gets401NotThrottledFirst()
    {
        await using var factory = new RateLimitedApiFactory();
        var client = factory.CreateClient();

        for (var i = 0; i < RateLimitedApiFactory.AiLimit + 2; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "/api/v1/ai-story-input/requests", "{}")).StatusCode);
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string url, string json) =>
        client.PostAsync(url, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
}
