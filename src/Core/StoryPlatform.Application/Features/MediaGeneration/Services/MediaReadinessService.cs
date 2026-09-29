using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

/// <summary>Single media completion rule used by both progress and finalization.</summary>
public sealed class MediaReadinessService : IMediaReadinessService
{
    private readonly IUnitOfWork _unitOfWork;

    public MediaReadinessService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<MediaReadinessResult> CheckAsync(int storyVersionId, int? expectedSceneCount,
        CancellationToken cancellationToken = default)
    {
        var scenes = await _unitOfWork.Repository<StoryScene>().FindAsync(
            x => x.StoryVersionId == storyVersionId, cancellationToken: cancellationToken);
        // EF parameter extraction on .NET 10 can bind array Contains to the span overload.
        // List<T>.Contains remains a stable, translatable instance method.
        var sceneIds = scenes.Select(x => x.Id).ToList();
        var beats = await _unitOfWork.Repository<IllustrationBeat>().FindAsync(
            x => sceneIds.Contains(x.StorySceneId), cancellationToken: cancellationToken);
        var segments = await _unitOfWork.Repository<StorySegment>().FindAsync(
            x => sceneIds.Contains(x.StorySceneId), cancellationToken: cancellationToken);
        var assets = await _unitOfWork.Repository<MediaAsset>().FindAsync(
            x => x.StoryVersionId == storyVersionId && x.StorySceneId != null,
            cancellationToken: cancellationToken);

        var scenesWithoutValidBeats = scenes.Where(scene =>
        {
            var sceneBeats = beats.Where(x => x.StorySceneId == scene.Id)
                .OrderBy(x => x.BeatOrder).ToArray();
            return sceneBeats.Length is < 1 or > 3 ||
                   sceneBeats.Where((beat, index) => beat.BeatOrder != index + 1).Any();
        }).Select(x => x.Id).ToArray();

        var missingBeats = beats.Where(beat => !assets.Any(asset =>
            asset.IllustrationBeatId == beat.Id && asset.StorySceneId == beat.StorySceneId &&
            asset.Type == MediaType.Illustration && asset.Status == MediaStatus.Ready &&
            asset.ValidationStatus == ValidationStatus.Passed)).Select(x => x.Id).ToArray();
        var missingAudio = segments.Where(segment => !assets.Any(asset =>
            asset.StorySegmentId == segment.Id && asset.Type == MediaType.TtsAudio &&
            asset.Status == MediaStatus.Ready)).Select(x => x.Id).ToArray();
        var manualReview = assets.Where(x => x.Status == MediaStatus.ManualReview)
            .Select(x => x.Id).ToArray();

        var ready = scenes.Count > 0 && (!expectedSceneCount.HasValue || scenes.Count == expectedSceneCount.Value) &&
                    scenesWithoutValidBeats.Length == 0 && missingBeats.Length == 0 &&
                    missingAudio.Length == 0 && manualReview.Length == 0;
        return new MediaReadinessResult(ready, scenes.Count, beats.Count,
            beats.Count(beat => !missingBeats.Contains(beat.Id)),
            segments.Count - missingAudio.Length,
            scenesWithoutValidBeats, missingBeats, missingAudio, manualReview);
    }
}
