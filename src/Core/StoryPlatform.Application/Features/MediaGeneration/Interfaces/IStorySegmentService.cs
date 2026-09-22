using System.Collections.Generic;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.MediaGeneration.Interfaces;

/// <summary>
/// Splits a story scene into audio segments (StorySegments) with character offsets.
/// Used to create sub-scene TTS units with word-level timing marks.
/// </summary>
public interface IStorySegmentService
{
    IReadOnlyList<StorySegment> CreateSegmentsForScene(StoryScene scene);
}
