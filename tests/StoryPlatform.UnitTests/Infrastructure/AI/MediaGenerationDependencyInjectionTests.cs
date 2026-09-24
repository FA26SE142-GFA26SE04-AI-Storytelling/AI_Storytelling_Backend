using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Infrastructure;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.AI;

public sealed class MediaGenerationDependencyInjectionTests
{
    [Fact]
    public void Registrations_resolve_semantic_segmenter_with_paragraph_fallback()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Vertex:UseVertex"] = "false",
                ["AI:MediaGeneration:WorkerEnabled"] = "false"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<ParagraphSceneSegmentationProvider>(
            provider.GetRequiredService<IParagraphSceneSegmentationProvider>());
        Assert.IsType<SemanticSceneSegmentationService>(
            provider.GetRequiredService<ISceneSegmentationProvider>());
    }

    [Theory]
    [InlineData("GeminiTTS")]
    [InlineData("gemini")]
    public void Registrations_resolve_configured_gemini_tts_provider(string configuredProvider)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Vertex:UseVertex"] = "false",
                ["AI:MediaGeneration:WorkerEnabled"] = "false",
                ["AI:TTS:Provider"] = configuredProvider
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<GeminiTtsProvider>(provider.GetRequiredService<ITtsProvider>());
    }

    [Fact]
    public void Registrations_reject_unsupported_tts_provider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Vertex:UseVertex"] = "false",
                ["AI:MediaGeneration:WorkerEnabled"] = "false",
                ["AI:TTS:Provider"] = "UnsupportedTts"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<ITtsProvider>());
        Assert.Contains("Unsupported AI:TTS:Provider", exception.Message);
    }
}
