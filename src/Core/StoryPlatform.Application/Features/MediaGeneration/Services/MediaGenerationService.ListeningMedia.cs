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
            asset.Url = await UploadGeneratedMediaAsync("ListeningAudioUpload", jobId, claimedToken, mediaContext.Id, asset,
                storagePath, audio, ct);
            asset.MimeType = audio.MimeType;
            // Chỉ phần cấp tương tác mà bé nhìn chữ theo giọng đọc mới cần mốc từng chữ (không phải audio đáp án);
            // nhà cung cấp không có mốc thì để trống.
            asset.WordTimings = ListeningAudioParts.NeedsWordTimings(interaction.Type, part.OptionId, part.PartKey)
                ? audio.GetMetadata("wordTimingsJson") : null;
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

    /// <summary>Audio đang tạo dở quá lâu (worker bị ngắt) thì coi như hỏng để tạo lại.</summary>
    private static readonly TimeSpan InstructionBackfillStale = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Tạo bù audio câu hướng dẫn cho tương tác đã có media từ trước khi có phần này (truyện cũ).
    /// Worker gọi lúc rảnh, mỗi lần một câu. Trả true nếu đã làm một câu (còn việc thì gọi tiếp ngay).
    /// Câu hướng dẫn do hệ thống viết sẵn nên không cần duyệt lại. Bỏ qua gói của phụ huynh vì manifest đã chốt
    /// danh sách media, và bỏ qua câu đã hết lượt thử.
    /// </summary>
    public async Task<bool> BackfillListeningInstructionAudioAsync(CancellationToken ct = default)
    {
        var target = FindInstructionBackfillTarget();
        if (target is null) return false;
        var (interaction, storyId) = target.Value;
        var text = ListeningAudioParts.InstructionFor(interaction)!;

        var asset = await ClaimInstructionAssetAsync(interaction, storyId, ct);
        if (asset is null) return true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.Audio.ProviderTimeoutSeconds, 1, 600)));
            var audio = await _ttsProvider.GenerateAsync(text, timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            var gate = _audioQualityGate.Validate(audio, SsmlTokenizer.Tokenize(text));
            if (!gate.IsPass) throw new InvalidOperationException(gate.Reason ?? "AUDIO_QUALITY_GATE_FAILED");

            var path = $"{storyId}/versions/{interaction.StoryVersionId}/listening-{interaction.Id}-i-{ListeningAudioParts.Instruction}-asset-{asset.Id}-{Guid.NewGuid():N}-backfill-a{asset.AttemptCount}{audio.SuggestedExtension}";
            await using (var content = audio.OpenReadStream())
                asset.Url = await _mediaStorage.UploadAsync(path, content, audio.MimeType, ct);
            asset.MimeType = audio.MimeType;
            asset.Provider = audio.GetMetadata("provider") ?? "GoogleCloud";
            asset.Model = audio.GetMetadata("model") ?? "unknown";
            asset.Status = MediaStatus.Ready;
            asset.ValidationStatus = ValidationStatus.Passed;
            asset.LastValidationReason = null;
            asset.CompletedAt = _clock.GetUtcNow().UtcDateTime;
        }
        catch (TransientMediaGenerationException exception) when (!ct.IsCancellationRequested)
        {
            // Nhà cung cấp tạm bận hoặc hết hạn mức: trả lượt thử, chờ vòng rảnh sau.
            asset.AttemptCount = Math.Max(0, asset.AttemptCount - 1);
            asset.Status = MediaStatus.Failed;
            asset.LastValidationReason = exception.ErrorCode;
            await SaveInstructionAssetAsync(asset, ct);
            return false;
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            asset.Status = MediaStatus.Failed;
            asset.ValidationStatus = ValidationStatus.Failed;
            asset.LastValidationReason = exception.Message.Length > 200 ? exception.Message[..200] : exception.Message;
            _logger.LogWarning(exception, "Listening instruction backfill failed Story={StoryId} Interaction={InteractionId} Asset={AssetId} Attempt={Attempt}",
                storyId, interaction.Id, asset.Id, asset.AttemptCount);
        }
        await SaveInstructionAssetAsync(asset, ct);
        return true;
    }

    private (ListeningInteraction Interaction, int StoryId)? FindInstructionBackfillTarget()
    {
        var interaction = InstructionBackfillCandidates(_unitOfWork.Repository<ListeningInteraction>().Query(),
            _unitOfWork.Repository<MediaAsset>().Query(), _assetMaxAttempts,
            _clock.GetUtcNow().UtcDateTime - InstructionBackfillStale).FirstOrDefault();
        if (interaction is null) return null;
        var storyId = _unitOfWork.Repository<StoryVersion>().Query()
            .Where(x => x.Id == interaction.StoryVersionId).Select(x => x.StoryId).First();
        return (interaction, storyId);
    }

    /// <summary>Tương tác cần tạo bù audio câu hướng dẫn, theo Id tăng dần. Public để test dịch được sang SQL.</summary>
    public static IQueryable<ListeningInteraction> InstructionBackfillCandidates(IQueryable<ListeningInteraction> interactions,
        IQueryable<MediaAsset> assets, int maxAttempts, DateTime staleBefore) =>
        interactions
            .Where(interaction => !interaction.IsDeleted && interaction.ParentStoryPackageId == null && interaction.StorySceneId != null
                // So từng loại thay vì array.Contains: C# 14 đổi Contains sang ReadOnlySpan, EF không dịch được.
                && (interaction.Type == ListeningInteractionType.RolePlay || interaction.Type == ListeningInteractionType.FillBlank
                    || interaction.Type == ListeningInteractionType.EventOrdering)
                // Media của tương tác đã tạo xong; chưa xong thì bước tạo media thường sẽ tạo luôn câu hướng dẫn.
                && assets.Any(a => a.ListeningInteractionId == interaction.Id && a.Type == MediaType.ListeningAudio
                    && a.ParentStoryPackageId == null && a.Status == MediaStatus.Ready && a.IsCurrent && !a.IsDeleted)
                && !assets.Any(a => a.ListeningInteractionId == interaction.Id && a.Type == MediaType.ListeningAudio
                    && a.ParentStoryPackageId == null && a.ListeningInteractionOptionId == null
                    && a.AudioPartKey == ListeningAudioParts.Instruction && a.IsCurrent && !a.IsDeleted
                    && (a.Status == MediaStatus.Ready || a.Status == MediaStatus.ManualReview || a.AttemptCount >= maxAttempts
                        || (a.Status == MediaStatus.Processing && (a.UpdatedAt ?? a.CreatedAt) > staleBefore))))
            .OrderBy(interaction => interaction.Id);

    // Giữ khoá truyện khi nhận việc để hai worker không cùng tạo một câu.
    private async Task<MediaAsset?> ClaimInstructionAssetAsync(ListeningInteraction interaction, int storyId, CancellationToken ct)
    {
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, ct);
            var now = _clock.GetUtcNow().UtcDateTime;
            var existing = (await _unitOfWork.Repository<MediaAsset>().FindAsync(x =>
                    x.StoryVersionId == interaction.StoryVersionId && x.ParentStoryPackageId == null &&
                    x.ListeningInteractionId == interaction.Id && x.ListeningInteractionOptionId == null &&
                    x.AudioPartKey == ListeningAudioParts.Instruction && x.Type == MediaType.ListeningAudio &&
                    x.IsCurrent && !x.IsDeleted, cancellationToken: ct))
                .OrderByDescending(x => x.Id).FirstOrDefault();
            var busy = existing is not null && (existing.Status is MediaStatus.Ready or MediaStatus.ManualReview ||
                existing.AttemptCount >= _assetMaxAttempts ||
                (existing.Status == MediaStatus.Processing && (existing.UpdatedAt ?? existing.CreatedAt) > now - InstructionBackfillStale));
            if (busy)
            {
                await _unitOfWork.CommitTransactionAsync(ct);
                return null;
            }

            var asset = existing ?? new MediaAsset
            {
                StoryVersionId = interaction.StoryVersionId,
                RequestedPackageRevision = (await FreshAsync<StoryVersion>(interaction.StoryVersionId, ct))!.MediaState.MediaPackageRevision,
                ListeningInteractionId = interaction.Id,
                AudioPartKey = ListeningAudioParts.Instruction,
                Type = MediaType.ListeningAudio
            };
            asset.Status = MediaStatus.Processing;
            asset.ValidationStatus = ValidationStatus.Pending;
            asset.AttemptCount++;
            asset.UpdatedAt = now;
            if (existing is null) await _unitOfWork.Repository<MediaAsset>().AddAsync(asset, ct);
            else _unitOfWork.Repository<MediaAsset>().Update(asset);
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
            return asset;
        }
        catch { await RollbackMediaAsync(CancellationToken.None); throw; }
    }

    private async Task SaveInstructionAssetAsync(MediaAsset asset, CancellationToken ct)
    {
        asset.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        _unitOfWork.Repository<MediaAsset>().Update(asset);
        await _unitOfWork.SaveChangesAsync(ct);
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
            .Where(ListeningMediaRequirements.HasOptionImages).ToArray();
        if (interactions.Length == 0) return;
        var options = await LoadListeningOptionsAsync(interactions, ct);
        foreach (var interaction in interactions)
        {
            var sceneId = interaction.StorySceneId ?? throw new InvalidOperationException("INTERACTION_SCENE_UNRESOLVED");
            var beat = (await _unitOfWork.Repository<IllustrationBeat>().FindAsync(x => x.StorySceneId == sceneId, cancellationToken: ct))
                .OrderBy(x => x.BeatOrder).FirstOrDefault() ?? throw new InvalidOperationException("INVALID_MEDIA_HANDOFF");
            foreach (var option in ListeningMediaRequirements.ImageOptions(interaction, options))
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
