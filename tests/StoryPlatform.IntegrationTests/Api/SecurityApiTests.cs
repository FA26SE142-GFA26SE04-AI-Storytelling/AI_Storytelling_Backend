using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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

/// <summary>
/// Production topology for the rate limiter: client → Cloudflare → cloudflared (sidecar in the same task, over
/// loopback) → API, with the visitor IP in CF-Connecting-IP. TestServer has no socket, so the connecting hop's IP
/// is taken from <see cref="ConnectingIpHeader"/> ahead of the real pipeline.
/// </summary>
public sealed class ProxiedRateLimitedApiFactory : ApiFactory
{
    public const string ConnectingIpHeader = "X-Test-Connecting-Ip";
    public const string ClientIpHeader = "CF-Connecting-IP";
    public const string TunnelIp = "127.0.0.1";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimiting:Auth:PermitLimit", RateLimitedApiFactory.AuthLimit.ToString());
        builder.UseSetting("RateLimiting:Auth:WindowSeconds", "300");
        builder.UseSetting("ForwardedHeaders:KnownNetworks:0", "127.0.0.1/32");
        builder.UseSetting("ForwardedHeaders:KnownNetworks:1", "::1/128");
        builder.UseSetting("ForwardedHeaders:ForwardLimit", "1");
        builder.UseSetting("ForwardedHeaders:ForwardedForHeaderName", ClientIpHeader);
        builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, ConnectingIpStartupFilter>());
    }

    private sealed class ConnectingIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (System.Net.IPAddress.TryParse(context.Request.Headers[ConnectingIpHeader], out var ip))
                    context.Connection.RemoteIpAddress = ip;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

/// <summary>DF06 6.2 – the auth limiter counts the real client IP behind the Cloudflare Tunnel, never a spoofed header.</summary>
public sealed class ForwardedClientIpRateLimitingApiTests
{
    private const string LoginUrl = "/api/v1/Auth/login";
    // The limiter runs before model validation, so a body without credentials still counts.
    private const string LoginBody = "{\"identifier\":\"nobody\"}";

    [Fact]
    public async Task SpoofedClientIpHeader_FromNonTunnelSource_IsIgnored()
    {
        await using var factory = new ProxiedRateLimitedApiFactory();
        var client = factory.CreateClient();

        for (var i = 0; i < RateLimitedApiFactory.AuthLimit; i++)
            Assert.NotEqual(HttpStatusCode.TooManyRequests,
                (await Login(client, "203.0.113.50", (ProxiedRateLimitedApiFactory.ClientIpHeader, $"198.51.100.{i + 1}"))).StatusCode);

        var rejected = await Login(client, "203.0.113.50", (ProxiedRateLimitedApiFactory.ClientIpHeader, "198.51.100.99"));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task ThroughTunnel_LimitAppliesToClientIp_AndOtherClientsKeepTheirQuota()
    {
        await using var factory = new ProxiedRateLimitedApiFactory();
        var client = factory.CreateClient();

        for (var i = 0; i < RateLimitedApiFactory.AuthLimit; i++)
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await ViaTunnel(client, "198.51.100.9")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await ViaTunnel(client, "198.51.100.9")).StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await ViaTunnel(client, "198.51.100.10")).StatusCode);
    }

    [Fact]
    public async Task ThroughTunnel_SpoofedXForwardedFor_DoesNotGrantNewQuota()
    {
        await using var factory = new ProxiedRateLimitedApiFactory();
        var client = factory.CreateClient();

        for (var i = 0; i <= RateLimitedApiFactory.AuthLimit; i++)
            await ViaTunnel(client, "198.51.100.9", spoofedForwardedFor: $"203.0.113.{i + 1}");

        var rejected = await ViaTunnel(client, "198.51.100.9", spoofedForwardedFor: "203.0.113.200");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    // Cloudflare's edge sets CF-Connecting-IP to the visitor IP; cloudflared passes it on over loopback.
    private static Task<HttpResponseMessage> ViaTunnel(HttpClient client, string clientIp, string? spoofedForwardedFor = null) =>
        spoofedForwardedFor is null
            ? Login(client, ProxiedRateLimitedApiFactory.TunnelIp, (ProxiedRateLimitedApiFactory.ClientIpHeader, clientIp))
            : Login(client, ProxiedRateLimitedApiFactory.TunnelIp, (ProxiedRateLimitedApiFactory.ClientIpHeader, clientIp),
                ("X-Forwarded-For", spoofedForwardedFor));

    private static Task<HttpResponseMessage> Login(HttpClient client, string connectingIp, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, LoginUrl)
        {
            Content = new StringContent(LoginBody, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add(ProxiedRateLimitedApiFactory.ConnectingIpHeader, connectingIp);
        foreach (var (name, value) in headers)
            request.Headers.Add(name, value);
        return client.SendAsync(request);
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
        var first = await factory.ClientForNewUserAsync(UserRole.User);
        var second = await factory.ClientForNewUserAsync(UserRole.User);
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
