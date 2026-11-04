using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ParentAuthoring;

/// <summary>Stable paragraph boundaries allow later children's interactions without changing scenes already delivered.</summary>
public static class ParentStoryPackageLayout
{
    public static async Task<IReadOnlyList<StoryScene>> EnsureAsync(IUnitOfWork uow, StoryVersion version, IStorySegmentService segments, CancellationToken ct)
    {
        var scenes = await uow.Repository<StoryScene>().FindAsync(x => x.StoryVersionId == version.Id && !x.IsDeleted, cancellationToken: ct);
        if (scenes.Count == 0)
        {
            scenes = new StoryBlockParser().Parse(version.Content!).Select((block, i) => new StoryScene { StoryVersionId = version.Id,
                SceneIndex = i, TextRangeStart = block.StartOffset, TextRangeEnd = block.EndOffset, SceneText = block.Text }).ToArray();
            await uow.Repository<StoryScene>().AddRangeAsync(scenes, ct); await uow.SaveChangesAsync(ct);
        }
        var position = 0;
        foreach (var scene in scenes.OrderBy(x => x.SceneIndex))
        {
            if (scene.TextRangeStart != position || scene.TextRangeEnd > version.Content!.Length ||
                version.Content[scene.TextRangeStart..scene.TextRangeEnd] != scene.SceneText) throw new ConflictException("INVALID_PERSISTED_SCENE_COVERAGE");
            position = scene.TextRangeEnd;
            if (!await uow.Repository<StorySegment>().ExistsAsync(x => x.StorySceneId == scene.Id && !x.IsDeleted, ct))
                await uow.Repository<StorySegment>().AddRangeAsync(segments.CreateSegmentsForScene(scene), ct);
        }
        if (position != version.Content!.Length) throw new ConflictException("INVALID_PERSISTED_SCENE_COVERAGE");
        await uow.SaveChangesAsync(ct); return scenes;
    }
}
