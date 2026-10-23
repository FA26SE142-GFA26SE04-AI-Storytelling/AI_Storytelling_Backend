using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed partial class MediaGenerationService
{
    private Task<T?> FreshAsync<T>(int id, CancellationToken ct) where T : BaseEntity =>
        _unitOfWork.Repository<T>().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken: ct);

    private async Task RollbackMediaAsync(CancellationToken ct)
    {
        try { await _unitOfWork.RollbackTransactionAsync(ct); }
        finally { _unitOfWork.DiscardTrackedChanges(); }
    }

    private async Task RecordAssetFailureAsync(int jobId, string token, int contextId, MediaAsset asset, CancellationToken ct)
    {
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var job = await RequireClaimedJobAsync(jobId, token, ct);
            await _unitOfWork.AcquireTransactionLockAsync(job.StoryId, ct);
            await AssertFreshAsync(jobId, token, asset.StoryVersionId, contextId, ct);
            var fresh = await FreshAsync<MediaAsset>(asset.Id, ct) ?? throw new InvalidOperationException("STALE_MEDIA_RESULT");
            fresh.Status = fresh.AttemptCount >= _assetMaxAttempts || asset.Status == MediaStatus.ManualReview ? MediaStatus.ManualReview : MediaStatus.Failed;
            fresh.ValidationStatus = asset.ValidationStatus;
            fresh.LastValidationReason = asset.LastValidationReason is { Length: > 500 } reason ? reason[..500] : asset.LastValidationReason;
            fresh.ValidationResultJson = asset.ValidationResultJson;
            _unitOfWork.Repository<MediaAsset>().Update(fresh);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch { await RollbackMediaAsync(CancellationToken.None); throw; }
    }
    private async Task StartAssetAttemptAsync(int jobId, string token, int contextId, MediaAsset asset, CancellationToken ct)
    {
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var job = await RequireClaimedJobAsync(jobId, token, ct);
            await _unitOfWork.AcquireTransactionLockAsync(job.StoryId, ct);
            await AssertFreshAsync(jobId, token, asset.StoryVersionId, contextId, ct);
            asset.Status = MediaStatus.Processing;
            asset.AttemptCount++;
            job.LeaseExpiresAt = _clock.GetUtcNow().UtcDateTime.Add(_jobLease);
            _unitOfWork.Repository<StoryGenerationJob>().Update(job);
            _unitOfWork.Repository<MediaAsset>().Update(asset);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch { await RollbackMediaAsync(CancellationToken.None); throw; }
    }
    private async Task<MediaAsset> ReplacementOrCurrentAsync(MediaAsset current, CancellationToken ct) =>
        await _unitOfWork.Repository<MediaAsset>().FirstOrDefaultAsync(x => x.ReplacesMediaAssetId == current.Id &&
            !x.IsCurrent && x.ReplacementResolvedAt == null, cancellationToken: ct) ?? current;

    private async Task EnsureNoPendingReplacementAsync(int versionId, CancellationToken ct)
    {
        if (await _unitOfWork.Repository<MediaAsset>().ExistsAsync(x => x.StoryVersionId == versionId &&
            x.ReplacesMediaAssetId != null && !x.IsCurrent && x.ReplacementResolvedAt == null, ct))
            throw new ConflictException("MEDIA_REPLACEMENT_UNRESOLVED");
    }

    private async Task PersistReadyAssetAsync(int jobId, string token, int contextId, MediaAsset asset, CancellationToken ct)
    {
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var job = await RequireClaimedJobAsync(jobId, token, ct);
            await _unitOfWork.AcquireTransactionLockAsync(job.StoryId, ct);
            await AssertFreshAsync(jobId, token, asset.StoryVersionId, contextId, ct);
            if (asset.ReplacesMediaAssetId is int oldId)
            {
                var candidate = await FreshAsync<MediaAsset>(asset.Id, ct);
                var previous = await FreshAsync<MediaAsset>(oldId, ct);
                if (candidate is null || candidate.ReplacementResolvedAt.HasValue ||
                    candidate.RequestedPackageRevision != job.MediaPackageRevision || previous?.IsCurrent != true ||
                    previous.StoryVersionId != asset.StoryVersionId || previous.Type != asset.Type ||
                    previous.IllustrationBeatId != asset.IllustrationBeatId || previous.StorySegmentId != asset.StorySegmentId)
                    throw new InvalidOperationException("STALE_MEDIA_RESULT");
                previous.IsCurrent = false;
                _unitOfWork.Repository<MediaAsset>().Update(previous);
                // Release the filtered unique key before selecting its replacement, in one transaction.
                await _unitOfWork.SaveChangesAsync(ct);
                asset.IsCurrent = true;
                asset.ReplacementResolvedAt = _clock.GetUtcNow().UtcDateTime;
            }
            _unitOfWork.Repository<MediaAsset>().Update(asset);
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch { await RollbackMediaAsync(CancellationToken.None); throw; }
    }

    private static string CommandHash(object command) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command))));

    private async Task<bool> ReplayMediaCommandAsync(int userId, int storyId, int versionId, string operationId, string hash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(operationId) || operationId.Length > 100) throw new BadRequestException("MEDIA_OPERATION_ID_REQUIRED");
        var receipt = await _unitOfWork.Repository<StoryAuthoringOperation>().FirstOrDefaultAsync(x =>
            x.StoryId == storyId && x.OperationId == operationId, cancellationToken: ct);
        if (receipt is null) return false;
        if (receipt.ActorUserId != userId || receipt.StoryVersionId != versionId || receipt.RequestHash != hash)
            throw new ConflictException("MEDIA_OPERATION_ID_CONFLICT");
        return true;
    }

    private Task<StoryAuthoringOperation> SaveMediaReceiptAsync(int userId, int storyId, int versionId, string operationId,
        string kind, string hash, int revision, CancellationToken ct) => _unitOfWork.Repository<StoryAuthoringOperation>().AddAsync(new()
        {
            StoryId = storyId, StoryVersionId = versionId, ActorUserId = userId, Kind = kind,
            OperationId = operationId, RequestHash = hash, OperationResponseJson = JsonSerializer.Serialize(new { packageRevision = revision })
        }, ct);

    private async Task RegenerateAsync(int userId, int storyId, int targetId, MediaType type, RegenerateMediaRequest input, CancellationToken ct)
    {
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, ct);
            var story = await LoadAuthorizedStoryAsync(userId, storyId, ct);
            await EnsureMutationPermissionAsync(story, userId, ct);
            await CreatorPublicationGuard.EnsureWritableAsync(_unitOfWork, storyId, null, ct);
            var version = await _unitOfWork.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.StoryId == storyId && x.IsCurrent, cancellationToken: ct)
                ?? throw new ConflictException("INVALID_MEDIA_HANDOFF");
            var kind = type == MediaType.Illustration ? "media-regenerate-image" : "media-regenerate-audio";
            var hash = CommandHash(new { kind, targetId, input });
            if (await ReplayMediaCommandAsync(userId, storyId, version.Id, input.OperationId, hash, ct))
            {
                await _unitOfWork.CommitTransactionAsync(ct);
                return;
            }
            if (story.Status is not (StoryStatus.MediaReview or StoryStatus.Ready)) throw new ConflictException("MEDIA_REGENERATION_NOT_ALLOWED");
            if (input.StoryVersionId != version.Id || input.PackageRevision != version.MediaState.MediaPackageRevision ||
                input.ExpectedConfirmed is null || input.ExpectedConfirmed != MediaPackageConfirmation.IsConfirmed(version))
                throw new ConflictException("STALE_MEDIA_PACKAGE");
            await EnsureNoActiveJobAsync(storyId, ct);
            await EnsureNoPendingReplacementAsync(version.Id, ct);
            await ValidateParentContextAsync(story, version.Id, ct);
            var parent = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.StoryId == storyId &&
                x.StoryVersionId == version.Id && x.Operation == GenerationJobOperation.GenerateMediaPackage, cancellationToken: ct)
                ?? throw new NotFoundException("Media job", storyId);
            var current = await _unitOfWork.Repository<MediaAsset>().FirstOrDefaultAsync(x => x.StoryVersionId == version.Id &&
                x.IsCurrent && x.Type == type && (type == MediaType.Illustration ? x.IllustrationBeatId == targetId : x.StorySegmentId == targetId), cancellationToken: ct)
                ?? throw new NotFoundException("Media asset", targetId);
            var scene = current.StorySceneId.HasValue ? await FreshAsync<StoryScene>(current.StorySceneId.Value, ct) : null;
            if (scene?.StoryVersionId != version.Id || current.Status != MediaStatus.Ready) throw new ConflictException("MEDIA_NOT_IN_CURRENT_VERSION");
            MediaPackageConfirmation.Invalidate(version);
            _unitOfWork.Repository<StoryVersion>().Update(version);
            await _unitOfWork.Repository<MediaAsset>().AddAsync(new()
            {
                StoryVersionId = version.Id, StorySceneId = current.StorySceneId, IllustrationBeatId = current.IllustrationBeatId,
                StorySegmentId = current.StorySegmentId, SceneIndex = current.SceneIndex, Type = type, IsCurrent = false,
                ReplacesMediaAssetId = current.Id, RequestedPackageRevision = version.MediaState.MediaPackageRevision
            }, ct);
            parent.RequestedByUserId = userId;
            parent.Status = GenerationJobStatus.Pending;
            parent.Stage = JobStage.MediaPending;
            parent.MediaContextId = null;
            parent.MediaPackageRevision = version.MediaState.MediaPackageRevision;
            parent.AttemptNo = 0;
            parent.NextAttemptAt = null;
            parent.LeaseExpiresAt = null;
            parent.CompletedAt = null;
            parent.ErrorCode = null;
            _unitOfWork.Repository<StoryGenerationJob>().Update(parent);
            RotateToken(parent);
            story.Status = StoryStatus.MediaProcessing;
            _unitOfWork.Repository<Story>().Update(story);
            await SaveMediaReceiptAsync(userId, storyId, version.Id, input.OperationId, kind, hash, version.MediaState.MediaPackageRevision, ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch { await RollbackMediaAsync(CancellationToken.None); throw; }
    }

    public async Task<MediaGenerationProgress> KeepCurrentAsync(int userId, int storyId, int candidateAssetId, KeepCurrentMediaRequest input, CancellationToken ct = default)
    {
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, ct);
            var story = await LoadAuthorizedStoryAsync(userId, storyId, ct);
            await EnsureMutationPermissionAsync(story, userId, ct);
            await CreatorPublicationGuard.EnsureWritableAsync(_unitOfWork, storyId, null, ct);
            var version = await _unitOfWork.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.StoryId == storyId && x.IsCurrent, cancellationToken: ct)
                ?? throw new ConflictException("INVALID_MEDIA_HANDOFF");
            var hash = CommandHash(new { kind = "media-keep-current", candidateAssetId, input });
            if (!await ReplayMediaCommandAsync(userId, storyId, version.Id, input.OperationId, hash, ct))
            {
                if (input.StoryVersionId != version.Id || input.PackageRevision != version.MediaState.MediaPackageRevision)
                    throw new ConflictException("STALE_MEDIA_PACKAGE");
                await EnsureNoActiveJobAsync(storyId, ct);
                await ValidateParentContextAsync(story, version.Id, ct);
                var candidate = await FreshAsync<MediaAsset>(candidateAssetId, ct);
                if (candidate is null || candidate.StoryVersionId != version.Id || candidate.IsCurrent || candidate.ReplacesMediaAssetId is null ||
                    candidate.RequestedPackageRevision != input.PackageRevision || candidate.ReplacementResolvedAt.HasValue ||
                    candidate.Status is not (MediaStatus.Failed or MediaStatus.ManualReview))
                    throw new ConflictException("MEDIA_REPLACEMENT_NOT_FAILED");
                var previous = await FreshAsync<MediaAsset>(candidate.ReplacesMediaAssetId.Value, ct);
                if (previous?.IsCurrent != true || previous.Status != MediaStatus.Ready) throw new ConflictException("MEDIA_PREVIOUS_ASSET_UNAVAILABLE");
                candidate.ReplacementResolvedAt = _clock.GetUtcNow().UtcDateTime;
                _unitOfWork.Repository<MediaAsset>().Update(candidate);
                await _unitOfWork.SaveChangesAsync(ct);
                if (!(await _readinessService.CheckAsync(version.Id, null, ct)).IsReady) throw new ConflictException("MEDIA_PACKAGE_INCOMPLETE");
                var parent = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.StoryVersionId == version.Id &&
                    x.Operation == GenerationJobOperation.GenerateMediaPackage, cancellationToken: ct) ?? throw new ConflictException("INVALID_MEDIA_HANDOFF");
                parent.Status = GenerationJobStatus.Completed;
                parent.Stage = JobStage.MediaCompleted;
                parent.CompletedAt = _clock.GetUtcNow().UtcDateTime;
                parent.ErrorCode = null;
                parent.FallbackMessage = null;
                _unitOfWork.Repository<StoryGenerationJob>().Update(parent);
                RotateToken(parent);
                story.Status = StoryStatus.MediaReview;
                _unitOfWork.Repository<Story>().Update(story);
                await SaveMediaReceiptAsync(userId, storyId, version.Id, input.OperationId, "media-keep-current", hash, input.PackageRevision, ct);
            }
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch { await RollbackMediaAsync(CancellationToken.None); throw; }
        return await GetProgressAsync(userId, storyId, ct);
    }
}
