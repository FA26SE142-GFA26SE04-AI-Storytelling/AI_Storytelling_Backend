using System.Text.Json;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class MediaGenerationFoundationTests
{
    private readonly StoryBlockParser _parser = new();
    private readonly SceneCoverageValidator _validator = new();

    [Fact]
    public void Parser_PreservesCanonicalContentExactly()
    {
        const string content = "Đoạn một.\n\nĐoạn hai.\nDòng tiếp.";
        var blocks = _parser.Parse(content);
        Assert.Equal(content, string.Concat(blocks.Select(x => x.Text)));
        Assert.Equal(0, blocks[0].StartOffset);
        Assert.Equal(content.Length, blocks[^1].EndOffset);
    }

    [Fact]
    public void Parser_UsesStableParagraphBlockIds()
    {
        var blocks = _parser.Parse("A\n\nB\n\nC");
        Assert.Equal(new[] { "P1", "P2", "P3" }, blocks.Select(x => x.BlockId));
    }

    [Fact]
    public void Parser_RejectsEmptyCanonicalContent() =>
        Assert.Throws<InvalidOperationException>(() => _parser.Parse(string.Empty));

    [Fact]
    public void Validator_AssemblesExactCanonicalRanges()
    {
        const string content = "A\n\nB\n\nC";
        var blocks = _parser.Parse(content);
        var scenes = _validator.ValidateAndAssemble(content, blocks, new[]
        {
            new SceneSelection(0, new[] { "P1", "P2" }, "opening"),
            new SceneSelection(1, new[] { "P3" }, "ending")
        });
        Assert.Equal(content, string.Concat(scenes.Select(x => x.SceneText)));
        Assert.All(scenes, scene => Assert.Equal(
            scene.SceneText, content[scene.TextRangeStart..scene.TextRangeEnd]));
    }

    [Fact]
    public void Validator_RejectsMissingBlock()
    {
        var blocks = _parser.Parse("A\n\nB");
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateAndAssemble(
            "A\n\nB", blocks, new[] { new SceneSelection(0, new[] { "P1" }) }));
    }

    [Fact]
    public void Validator_RejectsDuplicateBlock()
    {
        var blocks = _parser.Parse("A\n\nB");
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateAndAssemble(
            "A\n\nB", blocks, new[]
            {
                new SceneSelection(0, new[] { "P1" }),
                new SceneSelection(1, new[] { "P1", "P2" })
            }));
    }

    [Fact]
    public void Validator_RejectsOutOfOrderBlocks()
    {
        var blocks = _parser.Parse("A\n\nB");
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateAndAssemble(
            "A\n\nB", blocks, new[] { new SceneSelection(0, new[] { "P2", "P1" }) }));
    }

    [Fact]
    public void Validator_RejectsDuplicateSceneIndex()
    {
        var blocks = _parser.Parse("A\n\nB");
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateAndAssemble(
            "A\n\nB", blocks, new[]
            {
                new SceneSelection(0, new[] { "P1" }),
                new SceneSelection(0, new[] { "P2" })
            }));
    }

    [Fact]
    public async Task DefaultSegmenter_ReturnsBlockIdsWithoutRewritingText()
    {
        const string content = "A\n\nB";
        var blocks = _parser.Parse(content);
        var result = await new ParagraphSceneSegmentationProvider().SegmentAsync(
            new SceneSegmentationRequest(content, blocks, "{}"));
        Assert.Equal(blocks.Select(x => x.BlockId), result.SelectMany(x => x.BlockIds));
    }

    [Fact]
    public void MediaContext_IsPinnedToExactStoryVersion()
    {
        var json = new MediaContextBuilder().Build(new MediaContextBuildRequest(
            42, "Title", "Canonical", "Lesson", "Open", "Middle", "End", "{}", "{}"));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(42, document.RootElement.GetProperty("sourceStoryVersionId").GetInt32());
        Assert.Equal("Canonical", document.RootElement.GetProperty("storyFacts").GetProperty("canonicalContent").GetString());
    }

    [Fact]
    public void MediaContext_IsDeterministicForSameVersionAndInput()
    {
        var request = new MediaContextBuildRequest(1, "T", "C", null, null, null, null, "{}", "{}");
        var builder = new MediaContextBuilder();
        Assert.Equal(builder.Build(request), builder.Build(request));
    }

    [Fact]
    public void SceneSpecification_UsesExactSceneText()
    {
        var result = new SceneSpecificationBuilder().Build(1, 2, 0, "Exact text", "focus", "{}");
        Assert.Equal("Exact text", result.SceneText);
        Assert.Equal(2, result.StorySceneId);
        Assert.Contains("focus", result.MustShow);
    }

    [Fact]
    public async Task UnconfiguredEvaluator_FailsClosedInsteadOfReturningUncertain()
    {
        var evaluator = new FailClosedMediaEvaluator();
        var specification = new SceneSpecification(1, 2, 0, "text", null, "{}", [], []);

        var result = await evaluator.EvaluateAsync(
            specification, new GeneratedIllustration("https://media.test/image.png"));

        Assert.Equal(MediaEvaluationDecision.Fail, result.Decision);
        Assert.False(result.Passed);
    }
}
