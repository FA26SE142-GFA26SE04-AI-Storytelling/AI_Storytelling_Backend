using Microsoft.Extensions.Logging.Abstractions;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using Xunit;

namespace StoryPlatform.UnitTests;

/// <summary>
/// Phase 5 tests for the semantic scene segmentation orchestrator. Verifies
/// primary-fallback routing and that exceptions in the primary cause fallback.
/// </summary>
public sealed class SemanticSceneSegmentationServiceTests
{
    [Fact]
    public async Task Orchestrator_PrefersPrimaryProvider()
    {
        var primary = new RecordingSemanticProvider(new[]
        {
            new SceneSelection(0, new[] { "P1" }, "focus")
        });
        var fallback = new RecordingSemanticProvider(Array.Empty<SceneSelection>());
        var orchestrator = new SemanticSceneSegmentationService(
            primary, fallback, NullLogger<SemanticSceneSegmentationService>.Instance);

        var request = new SceneSegmentationRequest("A\n\nB", Array.Empty<StoryTextBlock>(), "{}");
        var result = await orchestrator.SegmentAsync(request);

        Assert.Single(result);
        Assert.Equal("focus", result[0].Focus);
        Assert.Equal(1, primary.Calls);
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task Orchestrator_FallsBackOnPrimaryException()
    {
        var primary = new ThrowingSemanticProvider(new InvalidOperationException("provider down"));
        var fallback = new RecordingSemanticProvider(new[]
        {
            new SceneSelection(0, new[] { "P1" })
        });
        var orchestrator = new SemanticSceneSegmentationService(
            primary, fallback, NullLogger<SemanticSceneSegmentationService>.Instance);

        var request = new SceneSegmentationRequest("A\n\nB", Array.Empty<StoryTextBlock>(), "{}");
        var result = await orchestrator.SegmentAsync(request);

        Assert.Single(result);
        Assert.Equal("P1", result[0].BlockIds[0]);
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task Orchestrator_FallsBackWhenPrimaryReturnsEmpty()
    {
        var primary = new RecordingSemanticProvider(Array.Empty<SceneSelection>());
        var fallback = new RecordingSemanticProvider(new[]
        {
            new SceneSelection(0, new[] { "P1" })
        });
        var orchestrator = new SemanticSceneSegmentationService(
            primary, fallback, NullLogger<SemanticSceneSegmentationService>.Instance);

        var request = new SceneSegmentationRequest("A\n\nB", Array.Empty<StoryTextBlock>(), "{}");
        var result = await orchestrator.SegmentAsync(request);

        Assert.Single(result);
        Assert.Equal(1, primary.Calls);
        Assert.Equal(1, fallback.Calls);
    }

    private sealed class RecordingSemanticProvider : ISemanticSceneSegmentationProvider, IParagraphSceneSegmentationProvider
    {
        private readonly IReadOnlyList<SceneSelection> _response;
        public int Calls { get; private set; }
        public RecordingSemanticProvider(IReadOnlyList<SceneSelection> response) => _response = response;
        public Task<IReadOnlyList<SceneSelection>> SegmentAsync(SceneSegmentationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_response);
        }
    }

    private sealed class ThrowingSemanticProvider : ISemanticSceneSegmentationProvider, IParagraphSceneSegmentationProvider
    {
        private readonly Exception _ex;
        public ThrowingSemanticProvider(Exception ex) => _ex = ex;
        public Task<IReadOnlyList<SceneSelection>> SegmentAsync(SceneSegmentationRequest request, CancellationToken cancellationToken = default)
            => throw _ex;
    }
}
