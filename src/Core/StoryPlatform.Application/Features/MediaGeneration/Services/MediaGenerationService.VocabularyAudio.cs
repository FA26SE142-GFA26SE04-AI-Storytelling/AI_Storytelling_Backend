using Microsoft.Extensions.Logging;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed partial class MediaGenerationService
{
    private async Task EnsureVocabularyAudioAsync(
        int jobId, string claimedToken, int storyId, int versionId, MediaContext mediaContext,
        StoryVocabulary word, CancellationToken cancellationToken)
    {
        var asset = await GetOrCreateVocabularyAudioAssetAsync(versionId, word, cancellationToken);
        if (asset.Status is MediaStatus.Ready or MediaStatus.ManualReview || asset.AttemptCount >= _assetMaxAttempts) return;

        var attempt = asset.AttemptCount;
        try
        {
            await AssertFreshAsync(jobId, claimedToken, versionId, mediaContext.Id, cancellationToken);
            await StartAssetAttemptAsync(jobId, claimedToken, mediaContext.Id, asset, cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.Audio.ProviderTimeoutSeconds, 1, 600)));
            var audio = await MeasureMediaStageAsync("VocabularyTtsGeneration", jobId, asset,
                () => _ttsProvider.GenerateAsync(word.Term, timeout.Token));
            timeout.Token.ThrowIfCancellationRequested();

            var gateResult = _audioQualityGate.Validate(audio, SsmlTokenizer.Tokenize(word.Term));
            if (!gateResult.IsPass)
            {
                asset.LastValidationReason = gateResult.Reason ?? "AUDIO_QUALITY_GATE_FAILED";
                asset.ValidationStatus = ValidationStatus.Failed;
                throw new InvalidOperationException("AUDIO_QUALITY_GATE_FAILED");
            }

            await AssertFreshAsync(jobId, claimedToken, versionId, mediaContext.Id, cancellationToken);
            var storagePath = $"{storyId}/versions/{versionId}/v{mediaContext.Revision}/vocab-{word.Id}-asset-{asset.Id}-{Guid.NewGuid():N}-a{attempt + 1}{audio.SuggestedExtension}";
            await using var content = audio.OpenReadStream();
            asset.Url = await MeasureMediaStageAsync("VocabularyAudioUpload", jobId, asset,
                () => _mediaStorage.UploadAsync(storagePath, content, audio.MimeType, cancellationToken));
            asset.MimeType = audio.MimeType;
            asset.Provider = audio.GetMetadata("provider") ?? "GoogleCloud";
            asset.Model = audio.GetMetadata("model") ?? "unknown";
            asset.Status = MediaStatus.Ready;
            asset.ValidationStatus = ValidationStatus.Passed;
            asset.CompletedAt = _clock.GetUtcNow().UtcDateTime;
            await PersistReadyAssetAsync(jobId, claimedToken, mediaContext.Id, asset, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && !IsStale(exception))
        {
            await RecordAssetFailureAsync(jobId, claimedToken, mediaContext.Id, asset, cancellationToken);
            _logger.LogWarning(exception,
                "Vocabulary audio skipped Story={StoryId} Version={VersionId} Vocabulary={VocabularyId} Asset={AssetId} Attempt={Attempt} Code={Code}",
                storyId, versionId, word.Id, asset.Id, asset.AttemptCount, PublicErrorCode(exception));
        }
    }

    private async Task<MediaAsset> GetOrCreateVocabularyAudioAssetAsync(
        int versionId, StoryVocabulary word, CancellationToken cancellationToken)
    {
        var existing = await _unitOfWork.Repository<MediaAsset>().FirstOrDefaultAsync(
            x => x.StoryVocabularyId == word.Id && x.Type == MediaType.VocabularyAudio && x.IsCurrent,
            cancellationToken: cancellationToken);
        if (existing is not null) return existing;
        var asset = new MediaAsset
        {
            StoryVersionId = versionId,
            RequestedPackageRevision = (await FreshAsync<StoryVersion>(versionId, cancellationToken))!.MediaState.MediaPackageRevision,
            StoryVocabularyId = word.Id,
            Type = MediaType.VocabularyAudio,
            Status = MediaStatus.Queued,
            ValidationStatus = ValidationStatus.Pending
        };
        await _unitOfWork.Repository<MediaAsset>().AddAsync(asset, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return asset;
    }
}
