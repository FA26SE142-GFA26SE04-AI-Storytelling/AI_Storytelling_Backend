using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StoryPlatform.AI.Api.Middleware;
using Xunit;

namespace StoryPlatform.AI.UnitTests;

/// <summary>
/// Locks the fail-closed behaviour of the Core -> AI internal credential.
/// A missing credential must never downgrade the AI service into an open endpoint.
/// </summary>
public sealed class InternalApiKeyMiddlewareTests : IDisposable
{
    private const string HeaderName = "X-Internal-Api-Key";
    private const string Credential = "test-internal-key-0123456789";

    private readonly string? _previousEnvironmentKey = Environment.GetEnvironmentVariable("AI_INTERNAL_API_KEY");
    public InternalApiKeyMiddlewareTests() => ClearProcessEnvironmentKey();
    public void Dispose() => Environment.SetEnvironmentVariable("AI_INTERNAL_API_KEY", _previousEnvironmentKey);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Blank_configuration_accepts_plain_environment_credential(string? configured)
    {
        Environment.SetEnvironmentVariable("AI_INTERNAL_API_KEY", " " + Credential + " ");
        var middleware = CreateMiddleware(new() { ["AI:InternalApiKey"] = configured }, "Development");
        var context = CreateContext("/api/ai/outline");
        context.Request.Headers[HeaderName] = Credential;
        await middleware.InvokeAsync(context);
        Assert.True(_nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Missing_credential_in_production_throws_at_startup()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateMiddleware(new Dictionary<string, string?>(), "Production"));

        Assert.Contains("AI:InternalApiKey", exception.Message);
    }

    [Fact]
    public async Task Missing_credential_in_staging_throws_at_startup()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateMiddleware(new Dictionary<string, string?>(), "Staging"));

        Assert.Contains("AI:InternalApiKey", exception.Message);
    }

    [Fact]
    public async Task Missing_credential_in_development_returns_503_instead_of_serving_traffic()
    {
        var middleware = CreateMiddleware(new Dictionary<string, string?>(), "Development");
        var context = CreateContext("/api/ai/outline");

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.False(context.Response.HasStarted && _nextCalled);
    }

    [Fact]
    public async Task Missing_credential_still_serves_health_check()
    {
        var middleware = CreateMiddleware(new Dictionary<string, string?>(), "Development");
        var context = CreateContext("/health");

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Missing_header_returns_401_when_credential_is_configured()
    {
        var middleware = CreateMiddleware(new Dictionary<string, string?> { ["AI:InternalApiKey"] = Credential }, "Development");
        var context = CreateContext("/api/ai/outline");

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task Wrong_header_returns_401_when_credential_is_configured()
    {
        var middleware = CreateMiddleware(new Dictionary<string, string?> { ["AI:InternalApiKey"] = Credential }, "Development");
        var context = CreateContext("/api/ai/outline");
        context.Request.Headers[HeaderName] = "wrong-key";

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task Correct_header_passes_through_to_the_next_middleware()
    {
        var middleware = CreateMiddleware(new Dictionary<string, string?> { ["AI:InternalApiKey"] = Credential }, "Development");
        var context = CreateContext("/api/ai/outline");
        context.Request.Headers[HeaderName] = Credential;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task Duplicate_header_is_rejected()
    {
        var middleware = CreateMiddleware(new Dictionary<string, string?> { ["AI:InternalApiKey"] = Credential }, "Development");
        var context = CreateContext("/api/ai/outline");
        context.Request.Headers.Append(HeaderName, Credential);
        context.Request.Headers.Append(HeaderName, Credential);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    private static bool _nextCalled;

    private static InternalApiKeyMiddleware CreateMiddleware(
        Dictionary<string, string?> settings,
        string environmentName)
    {
        _nextCalled = false;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var environment = new HostingEnvironmentStub(environmentName);
        return new InternalApiKeyMiddleware(
            _ => { _nextCalled = true; return Task.CompletedTask; },
            configuration,
            environment,
            NullLogger<InternalApiKeyMiddleware>.Instance);
    }

    /// <summary>
    /// The middleware also accepts a plain AI_INTERNAL_API_KEY environment variable, so the
    /// tests must clear it to stay hermetic regardless of the developer shell.
    /// </summary>
    internal static void ClearProcessEnvironmentKey() =>
        Environment.SetEnvironmentVariable("AI_INTERNAL_API_KEY", null);

    private static DefaultHttpContext CreateContext(string path)
    {
        var context = new DefaultHttpContext { TraceIdentifier = "test" };
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class HostingEnvironmentStub : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public HostingEnvironmentStub(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "StoryPlatform.AI.Api";
        public string WebRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
