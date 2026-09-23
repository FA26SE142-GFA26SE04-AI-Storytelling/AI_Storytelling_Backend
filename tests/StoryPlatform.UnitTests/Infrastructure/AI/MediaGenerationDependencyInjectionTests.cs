using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Infrastructure;
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
}

