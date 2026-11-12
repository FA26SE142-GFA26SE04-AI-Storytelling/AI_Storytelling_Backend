using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.AI;

/// <summary>Core gọi AI API trên Cloud Run phải kèm ID token của Google (Cloud Run IAM), ngoài khoá nội bộ.</summary>
public sealed class AiServiceIdTokenHandlerTests
{
    private sealed class FakeTokens(string? token) : IAiServiceIdTokenProvider
    {
        public int Calls { get; private set; }

        public Task<string?> GetIdTokenAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(token);
        }
    }

    private sealed class Capture : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static async Task<HttpRequestMessage> SendAsync(IAiServiceIdTokenProvider tokens)
    {
        var capture = new Capture();
        using var client = new HttpClient(new AiServiceIdTokenHandler(tokens) { InnerHandler = capture });
        client.DefaultRequestHeaders.Add("X-Internal-Api-Key", "internal");
        await client.GetAsync("https://ai.example.test/health");
        return capture.Request!;
    }

    [Fact]
    public async Task AddsBearerIdToken_AndKeepsTheInternalKeyHeader()
    {
        var request = await SendAsync(new FakeTokens("id-token-1"));

        Assert.Equal(("Bearer", "id-token-1"), (request.Headers.Authorization!.Scheme, request.Headers.Authorization.Parameter));
        Assert.Equal("internal", request.Headers.GetValues("X-Internal-Api-Key").Single());
    }

    [Fact]
    public async Task NoToken_SendsNoAuthorizationHeader()
    {
        var request = await SendAsync(new FakeTokens(null));

        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task Provider_WithoutAudience_ReturnsNull_SoLocalAndComposeAreUnchanged()
    {
        var provider = new GoogleAiServiceIdTokenProvider(
            Options.Create(new AIServiceOptions { BaseUrl = "http://ai-api:8080" }), NullLogger<GoogleAiServiceIdTokenProvider>.Instance);

        Assert.Null(await provider.GetIdTokenAsync());
    }

    [Fact]
    public async Task Provider_WithAudienceButBrokenCredentials_FailsLoudly()
    {
        var provider = new GoogleAiServiceIdTokenProvider(
            Options.Create(new AIServiceOptions { IdTokenAudience = "https://ai.example.test", GoogleCredentialsJson = "{ not json" }),
            NullLogger<GoogleAiServiceIdTokenProvider>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => provider.GetIdTokenAsync());
    }

    [Fact]
    public void DependencyInjection_BindsAudienceAndCredentialsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AIService:BaseUrl"] = "https://ai.example.test",
            ["AIService:IdTokenAudience"] = "https://ai.example.test",
            ["AIService:GoogleCredentialsJson"] = "{}"
        }).Build();
        var services = new ServiceCollection();
        services.Configure<AIServiceOptions>(configuration.GetSection(AIServiceOptions.SectionName));

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<AIServiceOptions>>().Value;

        Assert.Equal(("https://ai.example.test", "{}"), (options.IdTokenAudience, options.GoogleCredentialsJson));
    }
}
