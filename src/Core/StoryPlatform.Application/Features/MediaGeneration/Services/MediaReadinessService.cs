using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
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
            x => x.IsCurrent && x.StoryVersionId == storyVersionId && x.StorySceneId != null,
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

        var missingListening = await MissingListeningMediaAsync(storyVersionId, cancellationToken);

        var ready = scenes.Count > 0 && (!expectedSceneCount.HasValue || scenes.Count == expectedSceneCount.Value) &&
                    scenesWithoutValidBeats.Length == 0 && missingBeats.Length == 0 &&
                    missingAudio.Length == 0 && manualReview.Length == 0 && missingListening.Count == 0;
        return new MediaReadinessResult(ready, scenes.Count, beats.Count,
            beats.Count(beat => !missingBeats.Contains(beat.Id)),
            segments.Count - missingAudio.Length,
            scenesWithoutValidBeats, missingBeats, missingAudio, manualReview)
        {
            MissingListeningMedia = missingListening
        };
    }

    // Tương tác cần scene, audio Ready và ảnh Đoán tranh Ready đã qua kiểm tra.
    private async Task<IReadOnlyList<string>> MissingListeningMediaAsync(int storyVersionId, CancellationToken ct)
    {
        var interactions = await _unitOfWork.Repository<ListeningInteraction>().FindAsync(
            x => x.StoryVersionId == storyVersionId && !x.IsDeleted, cancellationToken: ct);
        if (interactions.Count == 0) return [];
        var ids = interactions.Select(x => x.Id).ToList();
        var options = await _unitOfWork.Repository<ListeningInteractionOption>().FindAsync(
            x => ids.Contains(x.ListeningInteractionId) && !x.IsDeleted, cancellationToken: ct);
        var assets = await _unitOfWork.Repository<MediaAsset>().FindAsync(x =>
            x.StoryVersionId == storyVersionId && x.IsCurrent && !x.IsDeleted && x.Status == MediaStatus.Ready &&
            (x.Type == MediaType.ListeningAudio || x.Type == MediaType.ListeningImage), cancellationToken: ct);

        var missing = new List<string>();
        foreach (var interaction in interactions.OrderBy(x => x.AnchorOffset))
        {
            if (interaction.StorySceneId is null) missing.Add($"{interaction.Id}:scene");
            foreach (var part in ListeningAudioParts.For(interaction, options))
                if (!assets.Any(a => a.Type == MediaType.ListeningAudio && a.ListeningInteractionId == interaction.Id &&
                        a.ListeningInteractionOptionId == part.OptionId && a.AudioPartKey == part.PartKey))
                    missing.Add($"{interaction.Id}:{(part.OptionId is int optionId ? $"o{optionId}" : "i")}:{part.PartKey}");
            if (interaction.Type != ListeningInteractionType.PicturePrediction) continue;
            foreach (var option in options.Where(x => x.ListeningInteractionId == interaction.Id).OrderBy(x => x.SortOrder))
                if (!assets.Any(a => a.Type == MediaType.ListeningImage && a.ListeningInteractionOptionId == option.Id &&
                        a.ValidationStatus == ValidationStatus.Passed))
                    missing.Add($"{interaction.Id}:o{option.Id}:image");
        }
        return missing;
    }
}
