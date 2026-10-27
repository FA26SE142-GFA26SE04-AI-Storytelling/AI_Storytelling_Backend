using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed partial class MediaGenerationService
{
    // A processor is scoped to one worker invocation and never shared between concurrent tasks.
    private (int Id, int BranchId, string Token)? _claimedImageTask;
    private bool _imageGenerationCompleted;

    private async Task AssertImageTaskClaimAsync(int branchId, CancellationToken ct)
    {
        if (_claimedImageTask is not { } claim || claim.BranchId != branchId) return;
        var task = await FreshAsync<MediaAssetGenerationTask>(claim.Id, ct);
        if (task is null || task.Status != GenerationJobStatus.Processing || task.ConcurrencyToken != claim.Token ||
            task.LeaseExpiresAt is null || task.LeaseExpiresAt <= _clock.GetUtcNow().UtcDateTime)
            throw new InvalidOperationException("STALE_JOB_RESULT");
        var branch = await FreshAsync<StoryGenerationJob>(branchId, ct);
        if (branch is null || task.StoryId != branch.StoryId || task.StoryVersionId != branch.StoryVersionId ||
            task.MediaContextId != branch.MediaContextId || task.PackageRevision != branch.MediaPackageRevision)
            throw new InvalidOperationException("STALE_MEDIA_RESULT");
    }

    // Caller owns the story lock and transaction. Ready assets are never requeued.
    private async Task QueueImageTasksAsync(StoryGenerationJob branch, CancellationToken ct)
    {
        var versionId = branch.StoryVersionId ?? throw new InvalidOperationException("INVALID_MEDIA_HANDOFF");
        var scenes = await _unitOfWork.Repository<StoryScene>().FindAsync(x => x.StoryVersionId == versionId, cancellationToken: ct);
        foreach (var scene in scenes)
        {
            var beats = await _unitOfWork.Repository<IllustrationBeat>().FindAsync(x => x.StorySceneId == scene.Id, cancellationToken: ct);
            foreach (var beat in beats)
            {
                var asset = await GetOrCreateIllustrationAssetAsync(versionId, scene, beat, ct);
                if (asset.Status == MediaStatus.Ready) continue;
                var task = await _unitOfWork.Repository<MediaAssetGenerationTask>().FirstOrDefaultAsync(
                    x => x.MediaAssetId == asset.Id, cancellationToken: ct);
                var added = task is null;
                if (task is null)
                {
                    task = new MediaAssetGenerationTask { StoryId = branch.StoryId, StoryVersionId = versionId,
                        BranchJobId = branch.Id, MediaAssetId = asset.Id, IllustrationBeatId = beat.Id };
                }
                else
                {
                    _unitOfWork.Repository<MediaAssetGenerationTask>().Update(task);
                    task.ConcurrencyToken = Guid.NewGuid().ToString("N");
                }
                task.MediaContextId = branch.MediaContextId!.Value;
                task.PackageRevision = branch.MediaPackageRevision!.Value;
                task.Status = GenerationJobStatus.Pending;
                task.QueuedAt = _clock.GetUtcNow().UtcDateTime;
                task.LeaseExpiresAt = task.NextAttemptAt = task.CompletedAt = null;
                task.ErrorCode = null;
                task.AttemptNo = 0;
                if (added) await _unitOfWork.Repository<MediaAssetGenerationTask>().AddAsync(task, ct);
                else _unitOfWork.Repository<MediaAssetGenerationTask>().Update(task);
            }
        }
        branch.Status = GenerationJobStatus.Processing;
        branch.Stage = JobStage.MediaGenerating;
        branch.LeaseExpiresAt = null;
        _unitOfWork.Repository<StoryGenerationJob>().Update(branch);
    }

    private async Task ReconcileImageBranchesAsync(CancellationToken ct)
    {
        var branches = await _unitOfWork.Repository<StoryGenerationJob>().FindAsync(x =>
            x.Operation == GenerationJobOperation.GenerateIllustration && x.ParentJobId != null &&
            (x.Status == GenerationJobStatus.Pending || x.Status == GenerationJobStatus.Processing), cancellationToken: ct);
        foreach (var observed in branches)
        {
            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                await _unitOfWork.AcquireTransactionLockAsync(observed.StoryId, ct);
                var branch = await FreshAsync<StoryGenerationJob>(observed.Id, ct);
                var parent = branch?.ParentJobId is int parentId ? await FreshAsync<StoryGenerationJob>(parentId, ct) : null;
                if (branch is null || branch.Status is not (GenerationJobStatus.Pending or GenerationJobStatus.Processing))
                { await RollbackMediaAsync(ct); continue; }
                if (parent?.Status == GenerationJobStatus.Pending || parent?.Status == GenerationJobStatus.Processing && parent.MediaContextId == null)
                { await RollbackMediaAsync(ct); continue; }
                if (parent?.Status != GenerationJobStatus.Processing || parent.MediaPackageRevision != branch.MediaPackageRevision)
                {
                    branch.Status = GenerationJobStatus.Cancelled;
                    _unitOfWork.Repository<StoryGenerationJob>().Update(branch);
                    await CancelImageTasksAsync(branch.Id, ct);
                    await _unitOfWork.CommitTransactionAsync(ct);
                    continue;
                }
                var tasks = await _unitOfWork.Repository<MediaAssetGenerationTask>().FindAsync(x =>
                    x.BranchJobId == branch.Id && x.PackageRevision == branch.MediaPackageRevision, cancellationToken: ct);
                if (tasks.Count == 0)
                {
                    // Recover packages prepared by the previous worker, without duplicating files.
                    if (branch.LeaseExpiresAt > _clock.GetUtcNow().UtcDateTime)
                    { await RollbackMediaAsync(ct); continue; }
                    branch.Status = GenerationJobStatus.Processing;
                    branch.LeaseExpiresAt = null;
                    RotateToken(branch);
                    await _unitOfWork.SaveChangesAsync(ct);
                    await AssertFreshAsync(branch.Id, branch.ConcurrencyToken, branch.StoryVersionId!.Value, branch.MediaContextId!.Value, ct);
                    await QueueImageTasksAsync(branch, ct);
                    RotateToken(branch);
                    await _unitOfWork.SaveChangesAsync(ct);
                }
                await AggregateImageBranchAsync(branch, ct);
                await _unitOfWork.CommitTransactionAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested &&
                (IsStale(ex) || IsTerminalBranchFailure(ex) || ex is ForbiddenException or ConflictException or NotFoundException))
            {
                await RollbackMediaAsync(CancellationToken.None);
                var parent = observed.ParentJobId is int parentId ? await FreshAsync<StoryGenerationJob>(parentId, ct) : null;
                if (parent?.Status == GenerationJobStatus.Processing)
                    await _failureFinalizer.MarkFailedAsync(parent.Id, parent.ConcurrencyToken, PublicErrorCode(ex), ct);
            }
            catch { await RollbackMediaAsync(CancellationToken.None); throw; }
        }
    }

    private async Task CancelImageTasksAsync(int branchId, CancellationToken ct)
    {
        var tasks = await _unitOfWork.Repository<MediaAssetGenerationTask>().FindAsync(x => x.BranchJobId == branchId &&
            (x.Status == GenerationJobStatus.Pending || x.Status == GenerationJobStatus.Processing), cancellationToken: ct);
        foreach (var task in tasks)
        {
            task.Status = GenerationJobStatus.Cancelled;
            task.LeaseExpiresAt = null;
            _unitOfWork.Repository<MediaAssetGenerationTask>().Update(task);
            task.ConcurrencyToken = Guid.NewGuid().ToString("N");
            _unitOfWork.Repository<MediaAssetGenerationTask>().Update(task);
        }
    }

    private async Task AggregateImageBranchAsync(StoryGenerationJob branch, CancellationToken ct)
    {
        var tasks = await _unitOfWork.Repository<MediaAssetGenerationTask>().FindAsync(x => x.BranchJobId == branch.Id &&
            x.PackageRevision == branch.MediaPackageRevision, cancellationToken: ct);
        if (tasks.Any(x => x.Status is GenerationJobStatus.Pending or GenerationJobStatus.Processing)) return;
        branch.Status = tasks.All(x => x.Status == GenerationJobStatus.Completed) ? GenerationJobStatus.Completed : GenerationJobStatus.Failed;
        branch.ErrorCode = tasks.FirstOrDefault(x => x.Status != GenerationJobStatus.Completed)?.ErrorCode;
        branch.CompletedAt = _clock.GetUtcNow().UtcDateTime;
        _unitOfWork.Repository<StoryGenerationJob>().Update(branch);
        RotateToken(branch);
    }

    private async Task<MediaJobProcessResult> ProcessImageTaskNextAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var candidates = await _unitOfWork.Repository<MediaAssetGenerationTask>().FindAsync(x =>
            (x.NextAttemptAt == null || x.NextAttemptAt <= now) && (x.Status == GenerationJobStatus.Pending ||
                x.Status == GenerationJobStatus.Processing && x.LeaseExpiresAt <= now), cancellationToken: ct);
        foreach (var candidate in candidates.OrderBy(x => x.NextAttemptAt ?? x.QueuedAt).ThenBy(x => x.Id))
        {
            StoryGenerationJob branch;
            MediaAssetGenerationTask task;
            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                await _unitOfWork.AcquireTransactionLockAsync(candidate.StoryId, ct);
                task = (await FreshAsync<MediaAssetGenerationTask>(candidate.Id, ct))!;
                now = _clock.GetUtcNow().UtcDateTime;
                if (task is null || task.ConcurrencyToken != candidate.ConcurrencyToken ||
                    task.NextAttemptAt > now || !(task.Status == GenerationJobStatus.Pending ||
                    task.Status == GenerationJobStatus.Processing && task.LeaseExpiresAt <= now))
                { await RollbackMediaAsync(ct); continue; }
                branch = (await FreshAsync<StoryGenerationJob>(task.BranchJobId, ct))!;
                var parent = branch?.ParentJobId is int parentId ? await FreshAsync<StoryGenerationJob>(parentId, ct) : null;
                if (parent?.Status == GenerationJobStatus.Pending || parent?.Status == GenerationJobStatus.Processing && parent.MediaContextId == null)
                { await RollbackMediaAsync(ct); continue; }
                if (branch?.Status != GenerationJobStatus.Processing || parent?.Status != GenerationJobStatus.Processing ||
                    branch.MediaPackageRevision != task.PackageRevision || parent.MediaPackageRevision != task.PackageRevision ||
                    branch.MediaContextId != task.MediaContextId)
                {
                    task.Status = GenerationJobStatus.Cancelled;
                    task.LeaseExpiresAt = null;
                    _unitOfWork.Repository<MediaAssetGenerationTask>().Update(task);
                    if (branch is not null && branch.MediaPackageRevision == task.PackageRevision &&
                        parent?.Status != GenerationJobStatus.Processing)
                    {
                        branch.Status = GenerationJobStatus.Cancelled;
                        RotateToken(branch);
                        await CancelImageTasksAsync(branch.Id, ct);
                    }
                    await _unitOfWork.CommitTransactionAsync(ct);
                    continue;
                }
                var running = await _unitOfWork.Repository<MediaAssetGenerationTask>().CountAsync(x => x.StoryId == task.StoryId &&
                    x.Status == GenerationJobStatus.Processing && x.LeaseExpiresAt > now, ct);
                if (running >= Math.Clamp(_options.MaxConcurrentImagesPerStory, 1, 8))
                { await RollbackMediaAsync(ct); continue; }
                _unitOfWork.Repository<MediaAssetGenerationTask>().Update(task);
                task.ConcurrencyToken = Guid.NewGuid().ToString("N");
                task.Status = GenerationJobStatus.Processing;
                task.StartedAt = now;
                task.LeaseExpiresAt = now.Add(_jobLease);
                task.NextAttemptAt = null;
                task.AttemptNo++;
                _unitOfWork.Repository<MediaAssetGenerationTask>().Update(task);
                await _unitOfWork.CommitTransactionAsync(ct);
            }
            catch { await RollbackMediaAsync(CancellationToken.None); throw; }

            _claimedImageTask = (task.Id, branch.Id, task.ConcurrencyToken);
            _imageGenerationCompleted = false;
            var elapsed = Stopwatch.StartNew();
            var token = branch.ConcurrencyToken;
            using var logScope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["StoryId"] = task.StoryId, ["StoryVersionId"] = task.StoryVersionId,
                ["PackageRevision"] = task.PackageRevision, ["MediaTaskId"] = task.Id, ["AssetId"] = task.MediaAssetId
            });
            try
            {
                await AssertFreshAsync(branch.Id, token, task.StoryVersionId, task.MediaContextId, ct);
                var asset = await FreshAsync<MediaAsset>(task.MediaAssetId, ct) ?? throw new InvalidOperationException("STALE_MEDIA_RESULT");
                var beat = await FreshAsync<IllustrationBeat>(task.IllustrationBeatId, ct) ?? throw new InvalidOperationException("STALE_MEDIA_RESULT");
                var scene = await FreshAsync<StoryScene>(beat.StorySceneId, ct) ?? throw new InvalidOperationException("STALE_MEDIA_RESULT");
                var context = await FreshAsync<MediaContext>(task.MediaContextId, ct) ?? throw new InvalidOperationException("STALE_MEDIA_RESULT");
                if (asset.StoryVersionId != task.StoryVersionId || asset.IllustrationBeatId != beat.Id || scene.StoryVersionId != task.StoryVersionId)
                    throw new InvalidOperationException("STALE_MEDIA_RESULT");
                var spec = _specificationBuilder.Build(task.StoryVersionId, scene.Id, scene.SceneIndex,
                    scene.SceneText, scene.VisualDescription, context.ContextJson);
                await EnsureIllustrationAsync(branch.Id, token, task.StoryId, task.StoryVersionId, context, scene, beat,
                    spec.ForBeat(scene.SceneText[beat.StartOffset..beat.EndOffset], beat.VisualFocus), ct);
                await FinishImageTaskAsync(task, branch, token, null, ct);
                return MediaJobProcessResult.Completed;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                if (IsProviderDeferral(ex))
                    _logger.LogInformation("Image task deferred Task={TaskId} Story={StoryId} Code={Code}", task.Id, task.StoryId, PublicErrorCode(ex));
                else
                    _logger.LogWarning(ex, "Image task failed Task={TaskId} Story={StoryId} Version={VersionId} Revision={Revision} Asset={AssetId} Code={Code}",
                        task.Id, task.StoryId, task.StoryVersionId, task.PackageRevision, task.MediaAssetId, PublicErrorCode(ex));
                await FinishImageTaskAsync(task, branch, token, ex, CancellationToken.None);
                return MediaJobProcessResult.Failed(IsPermanentFailure(ex), PublicErrorCode(ex));
            }
            finally
            {
                _logger.LogInformation("Media timing Task={TaskId} Story={StoryId} Version={VersionId} Revision={Revision} Asset={AssetId} Attempt={Attempt} QueueMs={QueueMs} TotalMs={TotalMs}",
                    task.Id, task.StoryId, task.StoryVersionId, task.PackageRevision, task.MediaAssetId, task.AttemptNo,
                    Math.Max(0, (task.StartedAt!.Value - task.QueuedAt).TotalMilliseconds), elapsed.Elapsed.TotalMilliseconds);
                _claimedImageTask = null;
            }
        }
        return MediaJobProcessResult.NoJob;
    }

    private async Task FinishImageTaskAsync(MediaAssetGenerationTask observed, StoryGenerationJob branch, string token, Exception? error, CancellationToken ct)
    {
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(observed.StoryId, ct);
            var task = await FreshAsync<MediaAssetGenerationTask>(observed.Id, ct);
            if (task is null || task.ConcurrencyToken != observed.ConcurrencyToken || task.Status != GenerationJobStatus.Processing ||
                task.LeaseExpiresAt <= _clock.GetUtcNow().UtcDateTime || error?.Message == "STALE_JOB_RESULT")
            { await RollbackMediaAsync(ct); return; }
            var asset = await FreshAsync<MediaAsset>(task.MediaAssetId, ct) ?? throw new InvalidOperationException("STALE_MEDIA_RESULT");
            if (error is null)
            {
                await AssertFreshAsync(branch.Id, token, task.StoryVersionId, task.MediaContextId, ct);
                task.Status = GenerationJobStatus.Completed;
                task.ErrorCode = null;
                task.CompletedAt = _clock.GetUtcNow().UtcDateTime;
            }
            else
            {
                var invalidated = IsStale(error) || error is ForbiddenException or ConflictException or NotFoundException;
                var deferred = IsProviderDeferral(error);
                if (deferred && asset.AttemptCount > 0)
                { asset.AttemptCount--; _unitOfWork.Repository<MediaAsset>().Update(asset); }
                if (deferred && task.AttemptNo > 0) task.AttemptNo--;
                var retry = !invalidated && !IsTerminalBranchFailure(error) && (deferred || asset.AttemptCount < _assetMaxAttempts);
                task.Status = retry ? GenerationJobStatus.Pending : GenerationJobStatus.Failed;
                task.ErrorCode = PublicErrorCode(error);
                var delay = deferred ? (error as TransientMediaGenerationException)?.RetryAfter ?? TimeSpan.FromSeconds(Math.Clamp(_options.Images.CircuitBreaker.DurationOfBreakSeconds, 10, 1800))
                    : TimeSpan.FromSeconds(Math.Pow(2, Math.Clamp(asset.AttemptCount, 1, 6)) + Random.Shared.NextDouble());
                if (error is TransientMediaGenerationException { RetryAfter: { } after } && after > delay) delay = after;
                delay = delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
                // Spread tasks sharing a provider resume deadline instead of waking them in a burst.
                if (deferred) delay += TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
                task.NextAttemptAt = retry ? _clock.GetUtcNow().UtcDateTime.Add(delay) : null;
                task.QueuedAt = _clock.GetUtcNow().UtcDateTime;
                task.CompletedAt = retry ? null : _clock.GetUtcNow().UtcDateTime;
                _logger.LogInformation("Image retry scheduled Task={TaskId} Story={StoryId} Asset={AssetId} Code={Code} Retry={Retry} Deferred={Deferred} Attempt={Attempt} RetryDelayMs={RetryDelayMs} NextAttemptAt={NextAttemptAt}",
                    task.Id, task.StoryId, task.MediaAssetId, task.ErrorCode, retry, deferred, asset.AttemptCount, delay.TotalMilliseconds, task.NextAttemptAt);
            }
            task.LeaseExpiresAt = null;
            _unitOfWork.Repository<MediaAssetGenerationTask>().Update(task);
            task.ConcurrencyToken = Guid.NewGuid().ToString("N");
            _unitOfWork.Repository<MediaAssetGenerationTask>().Update(task);
            await _unitOfWork.SaveChangesAsync(ct);
            var freshBranch = await FreshAsync<StoryGenerationJob>(branch.Id, ct);
            if (freshBranch?.ConcurrencyToken == token && freshBranch.Status == GenerationJobStatus.Processing)
                await AggregateImageBranchAsync(freshBranch, ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch { await RollbackMediaAsync(CancellationToken.None); throw; }
    }

    // An evaluator can also report an open circuit after an image was generated. That is
    // a real consumed generation attempt, not a request rejected before generation.
    private bool IsProviderDeferral(Exception error) => !_imageGenerationCompleted && error is TransientMediaGenerationException
        { ErrorCode: "MEDIA_CIRCUIT_OPEN" or "MEDIA_PROVIDER_COOLDOWN" };
}
