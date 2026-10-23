using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed partial class MediaGenerationService
{
    private static bool IsTerminalBranchFailure(Exception ex) => FindPermanentException(ex) is not null ||
        ex.Message.StartsWith("AUDIO_QUALITY_GATE_DETERMINISTIC_FAILURE", StringComparison.Ordinal) ||
        ex.Message is "INVALID_MEDIA_HANDOFF" or "INVALID_ILLUSTRATION_BEAT_ASSET";
    private static GenerationJobOperation BranchOperation(MediaType type) => type == MediaType.Illustration
        ? GenerationJobOperation.GenerateIllustration : GenerationJobOperation.GenerateTts;

    private async Task QueueBranchesAsync(MediaState state, MediaContext context, string token, CancellationToken ct)
    {
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(state.Story.Id, ct);
            await AssertFreshAsync(state.Job.Id, token, state.Version.Id, context.Id, ct);
            var parent = await RequireClaimedJobAsync(state.Job.Id, token, ct);
            var revision = state.Version.MediaState.MediaPackageRevision;
            foreach (var operation in new[] { GenerationJobOperation.GenerateIllustration, GenerationJobOperation.GenerateTts })
            {
                var child = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x =>
                    x.StoryVersionId == state.Version.Id && x.Operation == operation, cancellationToken: ct);
                var added = child is null;
                if (child is null)
                {
                    child = new StoryGenerationJob { StoryId = state.Story.Id, StoryVersionId = state.Version.Id,
                        RequestedByUserId = parent.RequestedByUserId, GenerationRequestId = parent.GenerationRequestId,
                        Operation = operation, Status = GenerationJobStatus.Pending, Stage = JobStage.MediaPending };
                    await _unitOfWork.Repository<StoryGenerationJob>().AddAsync(child, ct);
                }
                else if (child.MediaPackageRevision != revision || child.Status != GenerationJobStatus.Completed)
                {
                    child.Status = GenerationJobStatus.Pending;
                    child.CompletedAt = null;
                    child.ErrorCode = null;
                    child.NextAttemptAt = null;
                    child.LeaseExpiresAt = null;
                    _unitOfWork.Repository<StoryGenerationJob>().Update(child);
                    RotateToken(child);
                }
                child.RequestedByUserId = parent.RequestedByUserId;
                child.ParentJobId = parent.Id;
                child.MediaContextId = context.Id;
                child.MediaPackageRevision = revision;
                var replacement = await _unitOfWork.Repository<MediaAsset>().FirstOrDefaultAsync(x => x.StoryVersionId == state.Version.Id &&
                    x.ReplacesMediaAssetId != null && x.ReplacementResolvedAt == null && x.RequestedPackageRevision == revision,
                    cancellationToken: ct);
                if (replacement is not null && BranchOperation(replacement.Type) != operation)
                {
                    child.Status = GenerationJobStatus.Completed;
                    child.CompletedAt = _clock.GetUtcNow().UtcDateTime;
                }
                if (!added) _unitOfWork.Repository<StoryGenerationJob>().Update(child);
            }
            parent.MediaContextId = context.Id;
            parent.MediaPackageRevision = revision;
            parent.Stage = JobStage.MediaGenerating;
            parent.LeaseExpiresAt = null; // Branches own leases; the coordinator only aggregates.
            _unitOfWork.Repository<StoryGenerationJob>().Update(parent);
            RotateToken(parent);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch { await RollbackMediaAsync(CancellationToken.None); throw; }
    }

    public async Task<MediaJobProcessResult> ProcessBranchNextAsync(MediaType type, CancellationToken ct = default)
    {
        if (type is not (MediaType.Illustration or MediaType.TtsAudio)) throw new ArgumentOutOfRangeException(nameof(type));
        var now = _clock.GetUtcNow().UtcDateTime;
        var operation = BranchOperation(type);
        var child = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x =>
            x.ParentJobId != null && x.Operation == operation &&
            (x.NextAttemptAt == null || x.NextAttemptAt <= now) &&
            (x.Status == GenerationJobStatus.Pending || x.Status == GenerationJobStatus.Processing && x.LeaseExpiresAt < now),
            cancellationToken: ct);
        if (child is null) return MediaJobProcessResult.NoJob;
        var observed = child.ConcurrencyToken;
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(child.StoryId, ct);
            child = await FreshAsync<StoryGenerationJob>(child.Id, ct);
            if (child is null || child.ConcurrencyToken != observed ||
                child.Status is not (GenerationJobStatus.Pending or GenerationJobStatus.Processing))
            {
                await RollbackMediaAsync(ct);
                return MediaJobProcessResult.NoJob;
            }
            var parent = await FreshAsync<StoryGenerationJob>(child.ParentJobId!.Value, ct);
            if (parent is not null && (parent.Status == GenerationJobStatus.Pending ||
                parent.Status == GenerationJobStatus.Processing && parent.MediaContextId == null))
            {
                // Preparation/retry is not cancellation. Leave this claim untouched until the
                // coordinator publishes the context and runnable branches in one transaction.
                await RollbackMediaAsync(ct);
                return MediaJobProcessResult.NoJob;
            }
            if (parent?.Status != GenerationJobStatus.Processing || parent.MediaPackageRevision != child.MediaPackageRevision)
            {
                child.Status = GenerationJobStatus.Cancelled;
                _unitOfWork.Repository<StoryGenerationJob>().Update(child);
                await _unitOfWork.CommitTransactionAsync(ct);
                return MediaJobProcessResult.Completed;
            }
            child.Status = GenerationJobStatus.Processing;
            child.Stage = JobStage.MediaGenerating;
            child.StartedAt = now;
            child.LeaseExpiresAt = now.Add(_jobLease);
            child.NextAttemptAt = null;
            child.AttemptNo++;
            _unitOfWork.Repository<StoryGenerationJob>().Update(child);
            RotateToken(child);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch { await RollbackMediaAsync(CancellationToken.None); throw; }

        var token = child.ConcurrencyToken;
        try
        {
            var versionId = child.StoryVersionId ?? throw new InvalidOperationException("INVALID_MEDIA_HANDOFF");
            var context = await FreshAsync<MediaContext>(
                child.MediaContextId ?? throw new InvalidOperationException("INVALID_MEDIA_HANDOFF"), ct)
                ?? throw new InvalidOperationException("STALE_MEDIA_RESULT");
            await AssertFreshAsync(child.Id, token, versionId, context.Id, ct);
            var scenes = (await _unitOfWork.Repository<StoryScene>().FindAsync(x => x.StoryVersionId == versionId, cancellationToken: ct))
                .OrderBy(x => x.SceneIndex).ToArray();
            foreach (var scene in scenes)
            {
                if (type == MediaType.Illustration)
                {
                    var specification = _specificationBuilder.Build(versionId, scene.Id, scene.SceneIndex,
                        scene.SceneText, scene.VisualDescription, context.ContextJson);
                    var beats = await _unitOfWork.Repository<IllustrationBeat>().FindAsync(x => x.StorySceneId == scene.Id, cancellationToken: ct);
                    foreach (var beat in beats.OrderBy(x => x.BeatOrder))
                        await EnsureIllustrationAsync(child.Id, token, child.StoryId, versionId, context, scene, beat,
                            specification.ForBeat(scene.SceneText[beat.StartOffset..beat.EndOffset], beat.VisualFocus), ct);
                }
                else
                {
                    var segments = await _unitOfWork.Repository<StorySegment>().FindAsync(x => x.StorySceneId == scene.Id, cancellationToken: ct);
                    foreach (var segment in segments.OrderBy(x => x.SegmentOrder))
                        await EnsureSegmentAudioAsync(child.Id, token, child.StoryId, versionId, context, scene, segment, ct);
                }
            }
            await CompleteBranchAsync(child, token, context.Id, ct);
            return MediaJobProcessResult.Completed;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Media branch failed Job={JobId} Type={Type} Version={VersionId} Revision={Revision} Code={Code}",
                child.Id, type, child.StoryVersionId, child.MediaPackageRevision, PublicErrorCode(ex));
            await FailOrScheduleBranchAsync(child, token, type, ex, CancellationToken.None);
            return MediaJobProcessResult.Failed(IsPermanentFailure(ex), PublicErrorCode(ex));
        }
    }

    private async Task CompleteBranchAsync(StoryGenerationJob branch, string token, int contextId, CancellationToken ct)
    {
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(branch.StoryId, ct);
            await AssertFreshAsync(branch.Id, token, branch.StoryVersionId!.Value, contextId, ct);
            var fresh = await RequireClaimedJobAsync(branch.Id, token, ct);
            fresh.Status = GenerationJobStatus.Completed;
            fresh.CompletedAt = _clock.GetUtcNow().UtcDateTime;
            fresh.LeaseExpiresAt = null;
            fresh.ErrorCode = null;
            _unitOfWork.Repository<StoryGenerationJob>().Update(fresh);
            RotateToken(fresh);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch { await RollbackMediaAsync(CancellationToken.None); throw; }
    }

    private async Task FailOrScheduleBranchAsync(StoryGenerationJob branch, string token, MediaType type, Exception ex, CancellationToken ct)
    {
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(branch.StoryId, ct);
            var fresh = await FreshAsync<StoryGenerationJob>(branch.Id, ct);
            if (fresh?.ConcurrencyToken != token || fresh.Status != GenerationJobStatus.Processing)
            {
                await RollbackMediaAsync(ct);
                return;
            }
            if (ex.Message == "STALE_JOB_RESULT")
            {
                // An expired lease is recoverable by another claim. It does not invalidate the package.
                await RollbackMediaAsync(ct);
                return;
            }
            var assets = await _unitOfWork.Repository<MediaAsset>().FindAsync(x => x.StoryVersionId == branch.StoryVersionId &&
                x.Type == type && (x.IsCurrent || x.ReplacesMediaAssetId != null && x.ReplacementResolvedAt == null), cancellationToken: ct);
            var attempts = assets.Where(x => x.Status != MediaStatus.Ready).Select(x => x.AttemptCount).DefaultIfEmpty(0).Max();
            var invalidated = IsStale(ex) || ex is ForbiddenException or ConflictException or NotFoundException;
            var retry = !invalidated && !IsTerminalBranchFailure(ex) && attempts < _assetMaxAttempts;
            if (ex is TransientMediaGenerationException { ErrorCode: "MEDIA_CIRCUIT_OPEN" })
            {
                // No upstream request was made; do not consume the generation budget.
                var blocked = assets.FirstOrDefault(x => x.Status != MediaStatus.Ready && x.AttemptCount > 0);
                if (blocked is not null) { blocked.AttemptCount--; _unitOfWork.Repository<MediaAsset>().Update(blocked); }
                retry = true;
            }
            fresh.Status = retry ? GenerationJobStatus.Pending : GenerationJobStatus.Failed;
            fresh.ErrorCode = PublicErrorCode(ex);
            fresh.LeaseExpiresAt = null;
            fresh.NextAttemptAt = retry ? _clock.GetUtcNow().UtcDateTime.AddSeconds(ex is TransientMediaGenerationException { ErrorCode: "MEDIA_CIRCUIT_OPEN" }
                ? Math.Clamp((type == MediaType.Illustration ? _options.Images : _options.Audio).CircuitBreaker.DurationOfBreakSeconds, 10, 1800)
                : Math.Pow(2, Math.Clamp(attempts, 1, 6)) + Random.Shared.NextDouble()) : null;
            if (retry && ex is TransientMediaGenerationException { RetryAfter: { } retryAfter } &&
                fresh.NextAttemptAt < _clock.GetUtcNow().UtcDateTime.Add(retryAfter))
                fresh.NextAttemptAt = _clock.GetUtcNow().UtcDateTime.Add(retryAfter);
            fresh.CompletedAt = retry ? null : _clock.GetUtcNow().UtcDateTime;
            _unitOfWork.Repository<StoryGenerationJob>().Update(fresh);
            RotateToken(fresh);
            if (invalidated)
            {
                var parent = await FreshAsync<StoryGenerationJob>(branch.ParentJobId!.Value, ct);
                if (parent is not null)
                {
                    parent.Status = GenerationJobStatus.Failed;
                    parent.ErrorCode = fresh.ErrorCode;
                    parent.CompletedAt = _clock.GetUtcNow().UtcDateTime;
                    _unitOfWork.Repository<StoryGenerationJob>().Update(parent);
                    RotateToken(parent);
                }
                var siblings = await _unitOfWork.Repository<StoryGenerationJob>().FindAsync(x => x.ParentJobId == branch.ParentJobId &&
                    x.Id != branch.Id && (x.Status == GenerationJobStatus.Pending || x.Status == GenerationJobStatus.Processing), cancellationToken: ct);
                foreach (var sibling in siblings)
                {
                    sibling.Status = GenerationJobStatus.Cancelled;
                    sibling.LeaseExpiresAt = null;
                    _unitOfWork.Repository<StoryGenerationJob>().Update(sibling);
                    RotateToken(sibling);
                }
            }
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch { await RollbackMediaAsync(CancellationToken.None); throw; }
    }

    private async Task<bool> TryFinalizeNextAsync(CancellationToken ct)
    {
        var parents = await _unitOfWork.Repository<StoryGenerationJob>().FindAsync(x => x.Operation == GenerationJobOperation.GenerateMediaPackage &&
            x.Status == GenerationJobStatus.Processing && x.MediaContextId != null, cancellationToken: ct);
        foreach (var parent in parents)
        {
            var branches = await _unitOfWork.Repository<StoryGenerationJob>().FindAsync(x => x.ParentJobId == parent.Id &&
                x.MediaPackageRevision == parent.MediaPackageRevision, cancellationToken: ct);
            if (branches.Count != 2 || branches.Any(x => x.Status is GenerationJobStatus.Pending or GenerationJobStatus.Processing)) continue;
            if (branches.All(x => x.Status == GenerationJobStatus.Completed))
            {
                try
                {
                    var scenes = await _unitOfWork.Repository<StoryScene>().CountAsync(x => x.StoryVersionId == parent.StoryVersionId, ct);
                    await FinalizeAsync(parent.Id, parent.ConcurrencyToken, parent.StoryVersionId!.Value, parent.MediaContextId!.Value, scenes, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Media aggregate finalization failed Job={JobId} Code={Code}", parent.Id, PublicErrorCode(ex));
                    await _failureFinalizer.MarkFailedAsync(parent.Id, parent.ConcurrencyToken, PublicErrorCode(ex), CancellationToken.None);
                }
            }
            else
                await _failureFinalizer.MarkFailedAsync(parent.Id, parent.ConcurrencyToken,
                    branches.First(x => x.Status != GenerationJobStatus.Completed).ErrorCode ?? "MEDIA_PACKAGE_INCOMPLETE", ct);
            return true;
        }
        return false;
    }

    private async Task<MediaBranchProgress> BranchProgressAsync(StoryGenerationJob parent, MediaType type, int required, int ready, CancellationToken ct)
    {
        var branch = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.ParentJobId == parent.Id &&
            x.Operation == BranchOperation(type) && x.MediaPackageRevision == parent.MediaPackageRevision, cancellationToken: ct);
        return new(branch?.Status.ToString() ?? (parent.Status == GenerationJobStatus.Completed ? "Completed" : "Preparing"),
            required, ready, branch?.ErrorCode, branch?.NextAttemptAt);
    }
}
