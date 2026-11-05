using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed partial class MediaGenerationService
{
    private async Task<MediaJobProcessResult> ProcessCreatorChoiceTaskAsync(MediaType type, CancellationToken ct)
    {
        var image = type == MediaType.Illustration;
        var now = _clock.GetUtcNow().UtcDateTime;
        var observed = await _unitOfWork.Repository<MediaAssetGenerationTask>().FirstOrDefaultAsync(x =>
            (image ? x.Kind == MediaTaskKind.GenerateChosenImage || x.Kind == MediaTaskKind.ValidateUploadedImage
                : x.Kind == MediaTaskKind.GenerateChosenAudio || x.Kind == MediaTaskKind.ValidateUploadedAudio) &&
            (x.NextAttemptAt == null || x.NextAttemptAt <= now) && (x.Status == GenerationJobStatus.Pending ||
                x.Status == GenerationJobStatus.Processing && x.LeaseExpiresAt <= now), cancellationToken: ct);
        if (observed is null) return MediaJobProcessResult.NoJob;
        StoryGenerationJob root;
        MediaAssetGenerationTask task;
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(observed.StoryId, ct);
            task = await FreshAsync<MediaAssetGenerationTask>(observed.Id, ct) ?? throw new ConflictException("STALE_JOB_RESULT");
            if (task.ConcurrencyToken != observed.ConcurrencyToken || task.NextAttemptAt > now ||
                !(task.Status == GenerationJobStatus.Pending || task.Status == GenerationJobStatus.Processing && task.LeaseExpiresAt <= now))
            { await RollbackMediaAsync(ct); return MediaJobProcessResult.NoJob; }
            root = await FreshAsync<StoryGenerationJob>(task.BranchJobId, ct) ?? throw new ConflictException("STALE_JOB_RESULT");
            if (root.Status != GenerationJobStatus.Processing || CreatorMediaSelection.Mode(root) != "choose" ||
                root.MediaPackageRevision != task.PackageRevision || root.MediaContextId != task.MediaContextId)
            {
                task.Status = GenerationJobStatus.Cancelled; _unitOfWork.Repository<MediaAssetGenerationTask>().Update(task);
                await _unitOfWork.CommitTransactionAsync(ct); return MediaJobProcessResult.Completed;
            }
            await CreatorManualAsync(root.RequestedByUserId ?? 0, root.StoryId, ct);
            await AssertFreshAsync(root.Id, root.ConcurrencyToken, task.StoryVersionId, task.MediaContextId, ct);
            var running = await _unitOfWork.Repository<MediaAssetGenerationTask>().CountAsync(x => x.StoryId == task.StoryId &&
                x.Status == GenerationJobStatus.Processing && x.LeaseExpiresAt > now &&
                (image ? x.Kind == MediaTaskKind.GenerateChosenImage || x.Kind == MediaTaskKind.ValidateUploadedImage || x.Kind == MediaTaskKind.GenerateImage
                    : x.Kind == MediaTaskKind.GenerateChosenAudio || x.Kind == MediaTaskKind.ValidateUploadedAudio), ct);
            if (running >= (image ? Math.Clamp(_options.MaxConcurrentImagesPerStory, 1, 8) : 1))
            { await RollbackMediaAsync(ct); return MediaJobProcessResult.NoJob; }
            _unitOfWork.Repository<MediaAssetGenerationTask>().Update(task);
            task.Status = GenerationJobStatus.Processing; task.AttemptNo++; task.StartedAt = now;
            task.NextAttemptAt = null; task.LeaseExpiresAt = now.Add(_jobLease); task.ConcurrencyToken = Guid.NewGuid().ToString("N");
            _unitOfWork.Repository<MediaAssetGenerationTask>().Update(task);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is ForbiddenException or NotFoundException or ConflictException)
        {
            await RollbackMediaAsync(CancellationToken.None);
            var rejectedRoot = await FreshAsync<StoryGenerationJob>(observed.BranchJobId, ct);
            if (rejectedRoot?.Status == GenerationJobStatus.Processing && rejectedRoot.MediaPackageRevision == observed.PackageRevision)
                await _failureFinalizer.MarkFailedAsync(rejectedRoot.Id, rejectedRoot.ConcurrencyToken, PublicErrorCode(ex), CancellationToken.None);
            return MediaJobProcessResult.Failed(true, PublicErrorCode(ex));
        }
        catch { await RollbackMediaAsync(CancellationToken.None); throw; }
        _claimedImageTask = (task.Id, root.Id, task.ConcurrencyToken);
        _imageGenerationCompleted = false;
        var token = root.ConcurrencyToken;
        Exception? failure = null;
        try
        {
            var asset = await FreshAsync<MediaAsset>(task.MediaAssetId, ct) ?? throw new InvalidOperationException("STALE_MEDIA_RESULT");
            if (asset.StoryVersionId != task.StoryVersionId || asset.ParentStoryPackageId != null || asset.Type != type ||
                asset.Source != (task.Kind is MediaTaskKind.ValidateUploadedImage or MediaTaskKind.ValidateUploadedAudio ? MediaAssetSource.UserUpload : MediaAssetSource.AiGenerated))
                throw new InvalidOperationException("STALE_MEDIA_RESULT");
            var context = await FreshAsync<MediaContext>(task.MediaContextId, ct) ?? throw new InvalidOperationException("STALE_MEDIA_RESULT");
            var scene = image ? await SceneForBeatAsync(task.StoryVersionId, task.IllustrationBeatId!.Value, ct)
                : await SceneForSegmentAsync(task.StoryVersionId, asset.StorySegmentId!.Value, ct);
            var spec = _specificationBuilder.Build(task.StoryVersionId, scene.Id, scene.SceneIndex, scene.SceneText, scene.VisualDescription, context.ContextJson);
            if (image)
            {
                var beat = await FreshAsync<IllustrationBeat>(task.IllustrationBeatId!.Value, ct) ?? throw new InvalidOperationException("STALE_MEDIA_RESULT");
                spec = spec.ForBeat(scene.SceneText[beat.StartOffset..beat.EndOffset], beat.VisualFocus);
            }
            if (asset.Status == MediaStatus.Ready && asset.ValidationStatus == ValidationStatus.Passed && !string.IsNullOrWhiteSpace(asset.Url))
            {
                // A retry of supporting learning media must not validate or regenerate accepted narration again.
            }
            else if (asset.Source == MediaAssetSource.UserUpload)
            {
                await StartAssetAttemptAsync(root.Id, token, context.Id, asset, ct);
                var binary = await _mediaStorage.ReadAsync(asset.Url ?? throw new PermanentMediaGenerationException("MEDIA_UPLOAD_FILE_UNAVAILABLE"), ct);
                (_uploadValidator ?? throw new PermanentMediaGenerationException("MEDIA_UPLOAD_VALIDATOR_NOT_CONFIGURED")).Validate(binary, image);
                var metadata = JsonNode.Parse(asset.ValidationResultJson ?? "{}")!.AsObject();
                if (metadata["fileHash"]?.GetValue<string>() != Convert.ToHexString(SHA256.HashData(binary.Content)))
                    throw new PermanentMediaGenerationException("MEDIA_UPLOAD_HASH_MISMATCH");
                if (image)
                {
                    var alignment = await _alignmentEvaluator.EvaluateAsync(spec, binary, ct);
                    var safety = await _safetyEvaluator.EvaluateAsync(spec, binary, ct);
                    if (!alignment.Passed) throw new PermanentMediaGenerationException("UPLOADED_IMAGE_ALIGNMENT_FAILED");
                    if (!safety.Passed) throw new PermanentMediaGenerationException("UPLOADED_IMAGE_SAFETY_FAILED");
                    metadata["imageValidation"] = JsonNode.Parse(BuildValidationResultJson(alignment, safety));
                }
                else
                {
                    var structural = _audioQualityGate.Validate(binary, []);
                    if (!structural.IsPass) throw new PermanentMediaGenerationException("UPLOADED_AUDIO_INVALID");
                    metadata["requiresModeratorAudioReview"] = true;
                    asset.WordTimings = null;
                }
                metadata["technicalValidation"] = "Passed";
                asset.ValidationResultJson = metadata.ToJsonString(); asset.MimeType = binary.MimeType;
                asset.Status = MediaStatus.Ready; asset.ValidationStatus = ValidationStatus.Passed;
                asset.CompletedAt = _clock.GetUtcNow().UtcDateTime;
                await PersistReadyAssetAsync(root.Id, token, context.Id, asset, ct);
            }
            else if (image)
            {
                var beat = (await FreshAsync<IllustrationBeat>(task.IllustrationBeatId!.Value, ct))!;
                await EnsureIllustrationAsync(root.Id, token, task.StoryId, task.StoryVersionId, context, scene, beat, spec, ct);
            }
            else
                await EnsureSegmentAudioAsync(root.Id, token, task.StoryId, task.StoryVersionId, context, scene,
                    (await FreshAsync<StorySegment>(asset.StorySegmentId!.Value, ct))!, ct);
            if (!image)
                await EnsureCreatorLearningMediaAsync(root, token, context, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { failure = ex; }
        catch { _claimedImageTask = null; throw; }
        try
        {
            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                await _unitOfWork.AcquireTransactionLockAsync(task.StoryId, ct);
                var fresh = await FreshAsync<MediaAssetGenerationTask>(task.Id, ct);
                var currentRoot = await FreshAsync<StoryGenerationJob>(root.Id, ct);
                if (fresh?.ConcurrencyToken != task.ConcurrencyToken || currentRoot?.ConcurrencyToken != token ||
                    currentRoot.Status != GenerationJobStatus.Processing || currentRoot.MediaPackageRevision != task.PackageRevision)
                { await RollbackMediaAsync(ct); return MediaJobProcessResult.Failed(true, "STALE_JOB_RESULT"); }
                if (failure is null) await AssertFreshAsync(root.Id, token, task.StoryVersionId, task.MediaContextId, ct);
                var asset = await FreshAsync<MediaAsset>(task.MediaAssetId, ct);
                var generatedImage = task.Kind == MediaTaskKind.GenerateChosenImage;
                var deferred = failure is not null && generatedImage && IsProviderDeferral(failure);
                if (deferred)
                {
                    if (fresh.AttemptNo > 0) fresh.AttemptNo--;
                    if (asset is { AttemptCount: > 0 }) { asset.AttemptCount--; _unitOfWork.Repository<MediaAsset>().Update(asset); }
                }
                var retry = failure is not null && (generatedImage
                    ? ShouldRetryGeneratedImage(failure, asset?.AttemptCount ?? fresh.AttemptNo, deferred)
                    : failure is TransientMediaGenerationException or TimeoutException or OperationCanceledException && fresh.AttemptNo < _assetMaxAttempts);
                fresh.Status = failure is null ? GenerationJobStatus.Completed : retry ? GenerationJobStatus.Pending : GenerationJobStatus.Failed;
                fresh.ErrorCode = failure is null ? null : PublicErrorCode(failure); fresh.LeaseExpiresAt = null;
                fresh.NextAttemptAt = retry ? _clock.GetUtcNow().UtcDateTime.Add((failure as TransientMediaGenerationException)?.RetryAfter ?? TimeSpan.FromSeconds(60)) : null;
                fresh.CompletedAt = retry ? null : _clock.GetUtcNow().UtcDateTime;
                _unitOfWork.Repository<MediaAssetGenerationTask>().Update(fresh);
                if (failure is not null && asset is not null && asset.Status != MediaStatus.Ready)
                { asset.Status = MediaStatus.Failed; asset.LastValidationReason = fresh.ErrorCode; asset.ValidationStatus = ValidationStatus.Failed; _unitOfWork.Repository<MediaAsset>().Update(asset); }
                await _unitOfWork.CommitTransactionAsync(ct);
            }
            catch { await RollbackMediaAsync(CancellationToken.None); throw; }
        }
        finally { _claimedImageTask = null; }
        if (failure is not null) _logger.LogWarning("Creator media task failed Story={StoryId} Task={TaskId} Asset={AssetId} Code={Code}", task.StoryId, task.Id, task.MediaAssetId, PublicErrorCode(failure));
        return failure is null ? MediaJobProcessResult.Completed : MediaJobProcessResult.Failed(IsPermanentFailure(failure), PublicErrorCode(failure));
    }

    // Upload choices apply to story illustrations/narration; existing learning media still use Phase 5.
    // The audio task owns the lease, so provider errors retain the same durable retry path.
    private async Task EnsureCreatorLearningMediaAsync(StoryGenerationJob root, string token, MediaContext context, CancellationToken ct)
    {
        var version = root.StoryVersionId!.Value;
        var words = await _unitOfWork.Repository<StoryVocabulary>().FindAsync(x => x.StoryVersionId == version &&
            x.ParentStoryPackageId == null && !x.IsDeleted, cancellationToken: ct);
        foreach (var word in words.OrderBy(x => x.Id))
            await EnsureVocabularyAudioAsync(root.Id, token, root.StoryId, version, context, word, ct, required: true);
        await EnsureListeningAudioAsync(root.Id, token, root.StoryId, version, context, ct);
        var interactions = await LoadListeningInteractionsAsync(version, ct);
        var options = await LoadListeningOptionsAsync(interactions, ct);
        foreach (var interaction in interactions.Where(x => x.Type == ListeningInteractionType.PicturePrediction))
        {
            var scene = await FreshAsync<StoryScene>(interaction.StorySceneId!.Value, ct)
                ?? throw new InvalidOperationException("STALE_MEDIA_RESULT");
            var spec = _specificationBuilder.Build(version, scene.Id, scene.SceneIndex, scene.SceneText, scene.VisualDescription, context.ContextJson);
            foreach (var option in options.Where(x => x.ListeningInteractionId == interaction.Id))
            {
                var asset = await GetOrCreateClaimedListeningAssetAsync(root.Id, token, version, context.Id,
                    () => GetOrCreateListeningImageAssetAsync(version, interaction.Id, option.Id, ct), ct);
                await EnsureListeningImageAsync(root.Id, token, root.StoryId, context, asset, spec, ct);
            }
        }
    }
}
