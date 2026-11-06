using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

// Media của tương tác lúc nghe.
public sealed partial class MediaGenerationService
{
    private Task<IReadOnlyList<ListeningInteraction>> LoadListeningInteractionsAsync(int versionId, CancellationToken ct) =>
        _unitOfWork.Repository<ListeningInteraction>().FindAsync(
            x => x.StoryVersionId == versionId && x.ParentStoryPackageId == (_parentMediaScope == null ? null : (int?)_parentMediaScope.Id) && !x.IsDeleted, cancellationToken: ct);

    private async Task<IReadOnlyList<ListeningInteractionOption>> LoadListeningOptionsAsync(
        IReadOnlyList<ListeningInteraction> interactions, CancellationToken ct)
    {
        var ids = interactions.Select(x => x.Id).ToList();
        if (ids.Count == 0) return [];
        return await _unitOfWork.Repository<ListeningInteractionOption>().FindAsync(
            x => ids.Contains(x.ListeningInteractionId) && !x.IsDeleted, cancellationToken: ct);
    }

    // Gắn lại sau mỗi lần claim để phục hồi được job bị ngắt.
    private async Task BindListeningInteractionsAsync(
        MediaState state, string claimedToken, MediaContext mediaContext, IReadOnlyList<StoryScene> scenes,
        CancellationToken ct)
    {
        var interactions = await LoadListeningInteractionsAsync(state.Version.Id, ct);
        if (interactions.Count == 0) return;
        var sceneIds = ListeningInteractionSceneBinder.Bind(scenes, interactions);
        var changed = interactions.Where(x => x.StorySceneId != sceneIds[x.Id]).ToArray();
        if (changed.Length == 0) return;
        await AssertFreshAsync(state.Job.Id, claimedToken, state.Version.Id, mediaContext.Id, ct);
        foreach (var interaction in changed)
        {
            interaction.StorySceneId = sceneIds[interaction.Id];
            _unitOfWork.Repository<ListeningInteraction>().Update(interaction);
        }
        await _unitOfWork.SaveChangesAsync(ct);
    }

    // Nhánh TTS đọc mọi câu tương tác sau audio truyện và từ vựng.
    private async Task EnsureListeningAudioAsync(
        int jobId, string claimedToken, int storyId, int versionId, MediaContext mediaContext, CancellationToken ct)
    {
        var interactions = await LoadListeningInteractionsAsync(versionId, ct);
        if (interactions.Count == 0) return;
        var options = await LoadListeningOptionsAsync(interactions, ct);
        foreach (var interaction in interactions.OrderBy(x => x.AnchorOffset))
            foreach (var part in ListeningAudioParts.For(interaction, options))
                await EnsureListeningAudioPartAsync(jobId, claimedToken, storyId, versionId, mediaContext, interaction, part, ct);
    }

    private async Task EnsureListeningAudioPartAsync(
        int jobId, string claimedToken, int storyId, int versionId, MediaContext mediaContext,
        ListeningInteraction interaction, ListeningAudioPart part, CancellationToken ct)
    {
        var asset = await GetOrCreateClaimedListeningAssetAsync(jobId, claimedToken, versionId, mediaContext.Id,
            () => GetOrCreateListeningAudioAssetAsync(versionId, interaction.Id, part, ct), ct);
        if (ListeningMediaRequirements.IsUsable(asset)) return;
        if (asset.Status == MediaStatus.ManualReview || asset.AttemptCount >= _assetMaxAttempts)
            throw new InvalidOperationException("LISTENING_AUDIO_RETRY_EXHAUSTED");
        var attempt = asset.AttemptCount;
        try
        {
            await AssertFreshAsync(jobId, claimedToken, versionId, mediaContext.Id, ct);
            await StartAssetAttemptAsync(jobId, claimedToken, mediaContext.Id, asset, ct);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.Audio.ProviderTimeoutSeconds, 1, 600)));
            var audio = await MeasureMediaStageAsync("ListeningTtsGeneration", jobId, asset,
                () => _ttsProvider.GenerateAsync(part.Text, timeout.Token));
            timeout.Token.ThrowIfCancellationRequested();

            var gateResult = _audioQualityGate.Validate(audio, SsmlTokenizer.Tokenize(part.Text));
            if (!gateResult.IsPass)
            {
                var reason = gateResult.Reason ?? "AUDIO_QUALITY_GATE_FAILED";
                asset.LastValidationReason = reason;
                asset.ValidationStatus = ValidationStatus.Failed;
                if (IsDeterministicAudioFailure(reason))
                {
                    asset.Status = MediaStatus.ManualReview;
                    throw new InvalidOperationException($"AUDIO_QUALITY_GATE_DETERMINISTIC_FAILURE:{reason}");
                }
                asset.Status = MediaStatus.Failed;
                throw new InvalidOperationException("AUDIO_QUALITY_GATE_FAILED");
            }

            await AssertFreshAsync(jobId, claimedToken, versionId, mediaContext.Id, ct);
            var owner = part.OptionId is int optionId ? $"o{optionId}" : "i";
            var storagePath = $"{storyId}/versions/{versionId}/v{mediaContext.Revision}/listening-{interaction.Id}-{owner}-{part.PartKey}-asset-{asset.Id}-{Guid.NewGuid():N}-a{attempt + 1}{audio.SuggestedExtension}";
            await using var content = audio.OpenReadStream();
            asset.Url = await MeasureMediaStageAsync("ListeningAudioUpload", jobId, asset,
                () => _mediaStorage.UploadAsync(storagePath, content, audio.MimeType, ct));
            asset.MimeType = audio.MimeType;
            asset.Provider = audio.GetMetadata("provider") ?? "GoogleCloud";
            asset.Model = audio.GetMetadata("model") ?? "unknown";
            asset.Status = MediaStatus.Ready;
            asset.ValidationStatus = ValidationStatus.Passed;
            asset.CompletedAt = _clock.GetUtcNow().UtcDateTime;
            await PersistReadyAssetAsync(jobId, claimedToken, mediaContext.Id, asset, ct);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested && !IsStale(exception))
        {
            await RecordAssetFailureAsync(jobId, claimedToken, mediaContext.Id, asset, ct);
            _logger.LogWarning(exception,
                "Listening audio failed Story={StoryId} Version={VersionId} Interaction={InteractionId} Part={PartKey} Asset={AssetId} Attempt={Attempt}",
                storyId, versionId, interaction.Id, part.PartKey, asset.Id, asset.AttemptCount);
            throw;
        }
    }

    private async Task<MediaAsset> GetOrCreateListeningAudioAssetAsync(
        int versionId, int interactionId, ListeningAudioPart part, CancellationToken ct)
    {
        var optionId = part.OptionId;
        var key = part.PartKey;
        var packageId = _parentMediaScope?.Id;
        var candidates = await _unitOfWork.Repository<MediaAsset>().FindAsync(x =>
            x.StoryVersionId == versionId && x.ParentStoryPackageId == packageId &&
            x.ListeningInteractionId == interactionId && x.ListeningInteractionOptionId == optionId &&
            x.AudioPartKey == key && x.Type == MediaType.ListeningAudio && x.IsCurrent && !x.IsDeleted,
            cancellationToken: ct);
        var existing = ListeningMediaRequirements.FindAudio(candidates, interactionId, optionId, key) ??
            candidates.OrderByDescending(x => x.Id).FirstOrDefault();
        if (existing is not null) return existing;
        var asset = new MediaAsset
        {
            StoryVersionId = versionId,
            ParentStoryPackageId = _parentMediaScope?.Id,
            RequestedPackageRevision = _parentMediaScope?.Revision ?? (await FreshAsync<StoryVersion>(versionId, ct))!.MediaState.MediaPackageRevision,
            ListeningInteractionId = interactionId,
            ListeningInteractionOptionId = optionId,
            AudioPartKey = key,
            Type = MediaType.ListeningAudio,
            Status = MediaStatus.Queued,
            ValidationStatus = ValidationStatus.Pending
        };
        await _unitOfWork.Repository<MediaAsset>().AddAsync(asset, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return asset;
    }

    // Mỗi lựa chọn Đoán tranh dùng beat đầu của scene để lấy bối cảnh.
    private async Task QueueListeningImageTasksAsync(StoryGenerationJob branch, int versionId, CancellationToken ct)
    {
        var interactions = (await LoadListeningInteractionsAsync(versionId, ct))
            .Where(x => x.Type == ListeningInteractionType.PicturePrediction).ToArray();
        if (interactions.Length == 0) return;
        var options = await LoadListeningOptionsAsync(interactions, ct);
        foreach (var interaction in interactions)
        {
            var sceneId = interaction.StorySceneId ?? throw new InvalidOperationException("INTERACTION_SCENE_UNRESOLVED");
            var beat = (await _unitOfWork.Repository<IllustrationBeat>().FindAsync(x => x.StorySceneId == sceneId, cancellationToken: ct))
                .OrderBy(x => x.BeatOrder).FirstOrDefault() ?? throw new InvalidOperationException("INVALID_MEDIA_HANDOFF");
            foreach (var option in options.Where(x => x.ListeningInteractionId == interaction.Id).OrderBy(x => x.SortOrder))
            {
                var asset = await GetOrCreateListeningImageAssetAsync(versionId, interaction.Id, option.Id, ct);
                if (ListeningMediaRequirements.IsUsable(asset)) continue;
                await UpsertImageTaskAsync(branch, versionId, asset.Id, beat.Id, ct);
            }
        }
    }

    private async Task<MediaAsset> GetOrCreateListeningImageAssetAsync(
        int versionId, int interactionId, int optionId, CancellationToken ct)
    {
        var packageId = _parentMediaScope?.Id;
        var candidates = await _unitOfWork.Repository<MediaAsset>().FindAsync(x =>
            x.StoryVersionId == versionId && x.ParentStoryPackageId == packageId && x.ListeningInteractionId == interactionId &&
            x.ListeningInteractionOptionId == optionId && x.Type == MediaType.ListeningImage && x.IsCurrent && !x.IsDeleted,
            cancellationToken: ct);
        var existing = ListeningMediaRequirements.FindImage(candidates, optionId) ?? candidates.OrderByDescending(x => x.Id).FirstOrDefault();
        if (existing is not null) return existing;
        var asset = new MediaAsset
        {
            StoryVersionId = versionId,
            ParentStoryPackageId = _parentMediaScope?.Id,
            RequestedPackageRevision = _parentMediaScope?.Revision ?? (await FreshAsync<StoryVersion>(versionId, ct))!.MediaState.MediaPackageRevision,
            ListeningInteractionId = interactionId,
            ListeningInteractionOptionId = optionId,
            Type = MediaType.ListeningImage,
            Status = MediaStatus.Queued,
            ValidationStatus = ValidationStatus.Pending
        };
        await _unitOfWork.Repository<MediaAsset>().AddAsync(asset, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return asset;
    }

    // QueueListeningImageTasksAsync already owns this transaction and story lock. Direct generation uses this wrapper.
    private async Task<MediaAsset> GetOrCreateClaimedListeningAssetAsync(int jobId, string token, int versionId,
        int contextId, Func<Task<MediaAsset>> findOrCreate, CancellationToken ct)
    {
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var job = await RequireClaimedJobAsync(jobId, token, ct);
            await _unitOfWork.AcquireTransactionLockAsync(job.StoryId, ct);
            await AssertFreshAsync(jobId, token, versionId, contextId, ct);
            var asset = await findOrCreate();
            await _unitOfWork.CommitTransactionAsync(ct);
            return asset;
        }
        catch { await RollbackMediaAsync(CancellationToken.None); throw; }
    }

    private async Task EnsureListeningImageAsync(
        int jobId, string claimedToken, int storyId, MediaContext mediaContext,
        MediaAsset asset, SceneSpecification sceneSpecification, CancellationToken ct)
    {
        if (ListeningMediaRequirements.IsUsable(asset)) return;
        var option = asset.ListeningInteractionOptionId is int optionId
            ? await FreshAsync<ListeningInteractionOption>(optionId, ct) : null;
        if (option is null || option.IsDeleted || option.ListeningInteractionId != asset.ListeningInteractionId ||
            string.IsNullOrWhiteSpace(option.ImageDescription))
            throw new InvalidOperationException("STALE_MEDIA_RESULT");
        await GenerateImageAssetAsync(jobId, claimedToken, storyId, asset.StoryVersionId, mediaContext, asset,
            sceneSpecification.ForListeningOption(option.ImageDescription),
            $"listening-{asset.ListeningInteractionId}-o{option.Id}", ct);
    }
}
