using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Infrastructure;
using StoryPlatform.AI.Infrastructure.LLM.Gemini;
using StoryPlatform.AI.Infrastructure.LLM.VertexAI;
using Xunit;

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
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAIInfrastructure(Configuration(provider, authMode));
        using var scope = services.BuildServiceProvider();
        Assert.IsType(expected, scope.GetRequiredService<ILlmClient>());
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
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAIInfrastructure(configuration);
        using var scope = services.BuildServiceProvider();
        Assert.IsType<GeminiLlmClient>(scope.GetRequiredService<ILlmClient>());
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
