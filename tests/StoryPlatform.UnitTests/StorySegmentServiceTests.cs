using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Domain.Entities;
using Xunit;

namespace StoryPlatform.UnitTests;

/// <summary>
/// Phase 5 tests for StorySegmentService. Validates [start,end) offset convention
/// and round-trip preservation of paragraph text.
/// </summary>
public sealed class StorySegmentServiceTests
{
    [Fact]
    public void CreateSegmentsForScene_SingleParagraph_ReturnsOneSegment()
    {
        var service = new StorySegmentService();
        var scene = new StoryScene { Id = 1, SceneText = "Hello world." };

        var segments = service.CreateSegmentsForScene(scene);

        Assert.Single(segments);
        Assert.Equal(1, segments[0].SegmentOrder);
        Assert.Equal(0, segments[0].StartOffset);
        Assert.Equal("Hello world.".Length, segments[0].EndOffset);
        Assert.Equal("Hello world.", segments[0].TextContent);
    }

    [Fact]
    public void CreateSegmentsForScene_TwoParagraphs_ReturnsTwoSegments()
    {
        var service = new StorySegmentService();
        var text = "Ngày xưa.\n\nCó một con thỏ.";
        var scene = new StoryScene { Id = 1, SceneText = text };

        var segments = service.CreateSegmentsForScene(scene);

        Assert.Equal(2, segments.Count);
        Assert.Equal(1, segments[0].SegmentOrder);
        Assert.Equal(2, segments[1].SegmentOrder);
        Assert.Equal("Ngày xưa.", segments[0].TextContent);
        Assert.Equal("Có một con thỏ.", segments[1].TextContent);
        // [start, end) — second segment starts after separator.
        Assert.Equal(segments[0].EndOffset + 2, segments[1].StartOffset);
    }

    [Fact]
    public void CreateSegmentsForScene_EmptyText_ReturnsEmpty()
    {
        var service = new StorySegmentService();
        var scene = new StoryScene { Id = 1, SceneText = "" };

        var segments = service.CreateSegmentsForScene(scene);

        Assert.Empty(segments);
    }

    [Fact]
    public void CreateSegmentsForScene_SegmentTextRoundtripsFromScene()
    {
        var service = new StorySegmentService();
        const string text = "A\n\nB\n\nC";
        var scene = new StoryScene { Id = 1, SceneText = text };

        var segments = service.CreateSegmentsForScene(scene);

        foreach (var segment in segments)
        {
            var slice = text[segment.StartOffset..segment.EndOffset];
            Assert.Equal(slice, segment.TextContent);
        }
    }

    [Fact]
    public void CreateSegmentsForScene_CrlfSeparator_OffsetsAreAccurate()
    {
        var service = new StorySegmentService();
        var text = "Ngày xưa.\r\n\r\nCó một con thỏ.";
        var scene = new StoryScene { Id = 1, SceneText = text };

        var segments = service.CreateSegmentsForScene(scene);

        Assert.Equal(2, segments.Count);
        Assert.Equal("Ngày xưa.", segments[0].TextContent);
        Assert.Equal("Có một con thỏ.", segments[1].TextContent);
        // Second segment starts after the CRLFCRLF separator (4 chars), not 2.
        Assert.Equal(segments[0].EndOffset + 4, segments[1].StartOffset);
    }

    [Fact]
    public void CreateSegmentsForScene_CrlfSeparator_TextContentHasNoTrailingCarriageReturn()
    {
        var service = new StorySegmentService();
        var text = "A\r\n\r\nB";
        var scene = new StoryScene { Id = 1, SceneText = text };

        var segments = service.CreateSegmentsForScene(scene);

        Assert.Equal("A", segments[0].TextContent);
        Assert.Equal("B", segments[1].TextContent);
        Assert.DoesNotContain("\r", segments[0].TextContent);
        Assert.DoesNotContain("\r", segments[1].TextContent);
    }

    [Fact]
    public void CreateSegmentsForScene_WhitespaceOnlyParagraphs_AreSkipped()
    {
        var service = new StorySegmentService();
        // Three separators and a stray whitespace-only "paragraph" between two real ones.
        var text = "A\n\n   \n\nB";
        var scene = new StoryScene { Id = 1, SceneText = text };

        var segments = service.CreateSegmentsForScene(scene);

        Assert.Equal(2, segments.Count);
        Assert.Equal("A", segments[0].TextContent);
        Assert.Equal("B", segments[1].TextContent);
    }
}
