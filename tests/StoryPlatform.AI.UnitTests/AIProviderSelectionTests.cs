using Google.Apis.Auth.OAuth2;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Infrastructure;
using StoryPlatform.AI.Infrastructure.LLM.Gemini;
using StoryPlatform.AI.Infrastructure.LLM.VertexAI;
using Xunit;
using Type = System.Type;

namespace StoryPlatform.AI.UnitTests;

public sealed class AIProviderSelectionTests
{
    [Theory]
    [InlineData("Gemini", "Adc", typeof(GeminiLlmClient))]
    [InlineData("gemini", null, typeof(GeminiLlmClient))]
    [InlineData("Vertex", null, typeof(VertexAIGeminiClient))]
    [InlineData(null, "Adc", typeof(VertexAIGeminiClient))]
    [InlineData(null, null, typeof(GeminiLlmClient))]
    public void Explicit_provider_takes_precedence_over_auth_inference(string? provider, string? authMode, Type expected)
    {
        using var transport = new RejectNetworkTransport();
        using var sdk = OfflineSdk(transport);
        var services = Services(Configuration(provider, authMode), sdk);
        using var scope = services.BuildServiceProvider();
        var selected = scope.GetRequiredService<ILlmClient>();
        Assert.IsType(expected, selected);
        if (expected == typeof(VertexAIGeminiClient))
            Assert.Same(scope.GetRequiredService<VertexAIGeminiClient>(), selected);
        Assert.Equal(0, transport.Calls);
    }

    [Theory]
    [InlineData("UnsupportedProvider", "Adc")]
    [InlineData("UnsupportedProvider", null)]
    public void Invalid_explicit_provider_is_rejected_even_with_ADC(string provider, string? authMode)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddAIInfrastructure(Configuration(provider, authMode)));
        Assert.Contains("not supported", error.Message);
    }

    [Fact]
    public void Empty_primary_provider_uses_environment_alias_before_auth_inference()
    {
        var configuration = Configuration(" ", "Adc");
        configuration["AI_PROVIDER"] = "Gemini";
        using var transport = new RejectNetworkTransport();
        using var sdk = OfflineSdk(transport);
        var services = Services(configuration, sdk);
        using var scope = services.BuildServiceProvider();
        Assert.IsType<GeminiLlmClient>(scope.GetRequiredService<ILlmClient>());
        Assert.Equal(0, transport.Calls);
    }

    private static IServiceCollection Services(IConfiguration configuration, Client sdk)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAIInfrastructure(configuration);
        // Keep the production ILlmClient provider selector. Only substitute the SDK boundary,
        // otherwise resolving Vertex eagerly discovers ADC and makes unit tests machine-dependent.
        services.Replace(ServiceDescriptor.Singleton<VertexAIGeminiClient>(sp => new(
            sp.GetRequiredService<IOptions<VertexAIOptions>>(),
            sp.GetRequiredService<ILogger<VertexAIGeminiClient>>(), sdk)));
        return services;
    }

    private static Client OfflineSdk(RejectNetworkTransport transport) => new(vertexAI: true,
        project: "offline-test-project", location: "global",
        credential: GoogleCredential.FromAccessToken("offline-test-token"),
        httpOptions: new HttpOptions { RetryOptions = new HttpRetryOptions { Attempts = 1 } },
        clientOptions: new ClientOptions { HttpClientFactory = () => new HttpClient(transport, disposeHandler: false) });

    private sealed class RejectNetworkTransport : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            throw new InvalidOperationException("Provider selection must not make HTTP requests.");
        }
    }

    private static IConfigurationRoot Configuration(string? provider, string? authMode)
    {
        var values = new Dictionary<string, string?>
        {
            ["AI:Provider"] = provider,
            ["AI:Google:ProjectId"] = "offline-test-project", ["AI:Google:Location"] = "global",
            ["AI:Google:Model"] = "test-model", ["GEMINI_API_KEY"] = "test-only-placeholder"
        };
        if (authMode is not null) values["AI:Google:AuthMode"] = authMode;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
