using StoryPlatform.Application.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.ChildProfiles.Safety;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed partial class MediaGenerationService : IMediaGenerationService, IMediaGenerationJobProcessor
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediaContextBuilder _contextBuilder;
    private readonly IStoryBlockParser _blockParser;
    private readonly ISceneSegmentationProvider _segmenter;
    private readonly ISceneCoverageValidator _coverageValidator;
    private readonly ISceneSpecificationBuilder _specificationBuilder;
    private readonly IIllustrationBeatPlanner _beatPlanner;
    private readonly IImageGenerationProvider _imageProvider;
    private readonly ITtsProvider _ttsProvider;
    private readonly IMediaStorage _mediaStorage;
    private readonly IMediaAlignmentEvaluator _alignmentEvaluator;
    private readonly IMediaSafetyEvaluator _safetyEvaluator;
    private readonly IAudioQualityGate _audioQualityGate;
    private readonly IStorySegmentService _segmentService;
    private readonly IMediaGenerationJobFailureFinalizer _failureFinalizer;
    private readonly IMediaReadinessService _readinessService;
    private readonly ILogger<MediaGenerationService> _logger;
    private readonly ISupervisionAccessGuard? _supervision;
    private readonly TimeSpan _jobLease;
    private readonly int _assetMaxAttempts;
    private readonly MediaGenerationOptions _options;
    private readonly TimeProvider _clock;

    public MediaGenerationService(
        IUnitOfWork unitOfWork,
        IMediaContextBuilder contextBuilder,
        IStoryBlockParser blockParser,
        ISceneSegmentationProvider segmenter,
        ISceneCoverageValidator coverageValidator,
        ISceneSpecificationBuilder specificationBuilder,
        IIllustrationBeatPlanner beatPlanner,
        IImageGenerationProvider imageProvider,
        ITtsProvider ttsProvider,
        IMediaStorage mediaStorage,
        IMediaAlignmentEvaluator alignmentEvaluator,
        IMediaSafetyEvaluator safetyEvaluator,
        IAudioQualityGate audioQualityGate,
        IStorySegmentService segmentService,
        IMediaGenerationJobFailureFinalizer failureFinalizer,
        IMediaReadinessService readinessService,
        MediaGenerationOptions options,
        ILogger<MediaGenerationService> logger, ISupervisionAccessGuard? supervision = null, TimeProvider? clock = null)
    {
        _unitOfWork = unitOfWork;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _supervision = supervision;
        _contextBuilder = contextBuilder;
        _blockParser = blockParser;
        _segmenter = segmenter;
        _coverageValidator = coverageValidator;
        _specificationBuilder = specificationBuilder;
        _beatPlanner = beatPlanner;
        _imageProvider = imageProvider;
        _ttsProvider = ttsProvider;
        _mediaStorage = mediaStorage;
        _alignmentEvaluator = alignmentEvaluator;
        _safetyEvaluator = safetyEvaluator;
        _audioQualityGate = audioQualityGate;
        _segmentService = segmentService;
        _failureFinalizer = failureFinalizer;
        _readinessService = readinessService;
        _logger = logger;
        _jobLease = TimeSpan.FromSeconds(Math.Max(Math.Clamp(options.JobLeaseMinutes, 1, 30) * 60,
            Math.Clamp(Math.Max(options.Images.ProviderTimeoutSeconds, options.Audio.ProviderTimeoutSeconds), 1, 600) + 180));
        _assetMaxAttempts = Math.Clamp(options.AssetMaxAttempts, 1, 5);
    }

    public async Task<MediaGenerationProgress> GetProgressAsync(
        int userId, int storyId, CancellationToken cancellationToken = default)
    {
        var story = await LoadAuthorizedStoryAsync(userId, storyId, cancellationToken);
        var currentVersion = await _unitOfWork.Repository<StoryVersion>().FirstOrDefaultAsync(
            x => x.StoryId == storyId && x.IsCurrent && !x.IsDeleted, cancellationToken: cancellationToken);
        var job = (await _unitOfWork.Repository<StoryGenerationJob>().FindAsync(
                x => x.StoryId == storyId && x.Operation == GenerationJobOperation.GenerateMediaPackage &&
                     currentVersion != null && x.StoryVersionId == currentVersion.Id,
                cancellationToken: cancellationToken))
            .OrderByDescending(x => x.Id).FirstOrDefault();
        if (job?.StoryVersionId is not int versionId)
            return new MediaGenerationProgress(storyId, null, story.Status.ToString(), "NotStarted", 0, 0, 0, false, null);
        var readiness = await _readinessService.CheckAsync(versionId, expectedSceneCount: null, cancellationToken);
        var version = await FreshAsync<StoryVersion>(versionId, cancellationToken)
                      ?? throw new NotFoundException("StoryVersion", versionId);
        var confirmed = MediaPackageConfirmation.IsConfirmed(version);
        return new MediaGenerationProgress(
            storyId, versionId, story.Status.ToString(), job.Status.ToString(), readiness.SceneCount,
            readiness.ReadyIllustrations, readiness.ReadyAudio,
            story.Status == StoryStatus.Ready && readiness.IsReady && confirmed, job.ErrorCode, readiness.RequiredIllustrations,
            readiness.MissingIllustrationBeatIds, readiness.MissingAudioSegmentIds,
            readiness.ManualReviewAssetIds, readiness.ScenesWithoutValidBeats)
        {
            PackageRevision = version.MediaState.MediaPackageRevision,
            IsGenerated = readiness.IsReady,
            IsAwaitingConfirmation = story.Status == StoryStatus.MediaReview && readiness.IsReady && !confirmed,
            IsConfirmed = readiness.IsReady && confirmed,
            ConfirmedByUserId = version.MediaState.MediaConfirmedByUserId,
            ConfirmedAt = version.MediaState.MediaConfirmedAt,
            Images = await BranchProgressAsync(job, MediaType.Illustration, readiness.RequiredIllustrations, readiness.ReadyIllustrations, cancellationToken),
            Audio = await BranchProgressAsync(job, MediaType.TtsAudio, readiness.ReadyAudio + readiness.MissingAudioSegmentIds.Count, readiness.ReadyAudio, cancellationToken)
        };
    }

    public async Task<StoryMediaPackage> GetPackageAsync(int userId, int storyId,
        CancellationToken cancellationToken = default)
    {
        var story = await LoadAuthorizedStoryAsync(userId, storyId, cancellationToken);
        var version = (await _unitOfWork.Repository<StoryVersion>().FindAsync(
            x => x.StoryId == storyId && x.IsCurrent, cancellationToken: cancellationToken)).FirstOrDefault()
            ?? throw new NotFoundException("Current story version", storyId);
        var scenes = (await _unitOfWork.Repository<StoryScene>().FindAsync(
            x => x.StoryVersionId == version.Id, cancellationToken: cancellationToken))
            .OrderBy(x => x.SceneIndex).ToArray();
        var sceneIds = scenes.Select(scene => scene.Id).ToList();
        var beats = await _unitOfWork.Repository<IllustrationBeat>().FindAsync(
            x => sceneIds.Contains(x.StorySceneId), cancellationToken: cancellationToken);
        var assets = await _unitOfWork.Repository<MediaAsset>().FindAsync(
            x => x.StoryVersionId == version.Id,
            cancellationToken: cancellationToken);
        var segments = await _unitOfWork.Repository<StorySegment>().FindAsync(
            x => sceneIds.Contains(x.StorySceneId), cancellationToken: cancellationToken);
        var package = new StoryMediaPackage(storyId, version.Id, scenes.Select(scene =>
            new SceneMediaItem(scene.Id, scene.SceneIndex, beats.Where(b => b.StorySceneId == scene.Id)
                .OrderBy(b => b.BeatOrder).Select(beat =>
                {
                    var asset = assets.FirstOrDefault(a => a.IsCurrent && a.IllustrationBeatId == beat.Id && a.Type == MediaType.Illustration);
                    return new IllustrationBeatMediaItem(beat.Id, beat.BeatOrder, beat.StartOffset, beat.EndOffset,
                        beat.VisualFocus, asset?.Id, asset?.Status.ToString() ?? "Queued", asset?.Url);
                }).ToArray())
            {
                Text = scene.SceneText,
                Audio = segments.Where(x => x.StorySceneId == scene.Id).OrderBy(x => x.SegmentOrder).Select(segment =>
                {
                    var audio = assets.FirstOrDefault(x => x.IsCurrent && x.StorySegmentId == segment.Id && x.Type == MediaType.TtsAudio);
                    return new AudioMediaItem(segment.Id, segment.SegmentOrder, segment.TextContent, audio?.Id,
                        audio?.Status.ToString() ?? "Queued", audio?.Url, audio?.WordTimings);
                }).ToArray()
            }).ToArray());
        var readiness = await _readinessService.CheckAsync(version.Id, null, cancellationToken);
        var confirmed = MediaPackageConfirmation.IsConfirmed(version);
        package = package with
        {
            PackageRevision = version.MediaState.MediaPackageRevision,
            IsGenerated = readiness.IsReady,
            IsAwaitingConfirmation = story.Status == StoryStatus.MediaReview && readiness.IsReady && !confirmed,
            IsConfirmed = readiness.IsReady && confirmed,
            IsReady = story.Status == StoryStatus.Ready && readiness.IsReady && confirmed,
            ConfirmedByUserId = version.MediaState.MediaConfirmedByUserId,
            ConfirmedAt = version.MediaState.MediaConfirmedAt
        };
        package = package with { Replacements = assets.Where(x => x.ReplacesMediaAssetId.HasValue &&
            x.RequestedPackageRevision == version.MediaState.MediaPackageRevision).Select(x => new MediaReplacementItem(
                x.Id, x.ReplacesMediaAssetId!.Value, x.Type.ToString(), x.IllustrationBeatId ?? x.StorySegmentId!.Value,
                x.IsCurrent ? "Applied" : x.ReplacementResolvedAt.HasValue ? "Kept" :
                x.Status is MediaStatus.Failed or MediaStatus.ManualReview ? "Failed" : "Generating")).ToArray() };
        return await SignPackageAsync(package, cancellationToken,
            assets.Where(x => x.RequestedPackageRevision == null && x.Status == MediaStatus.Ready && x.Url != null)
                .Select(x => x.Url!).ToHashSet(StringComparer.Ordinal));
    }

    private async Task<StoryMediaPackage> SignPackageAsync(StoryMediaPackage package, CancellationToken ct, IReadOnlySet<string> legacyPaths)
    {
        var scenes = new List<SceneMediaItem>();
        foreach (var scene in package.Scenes)
        {
            var images = new List<IllustrationBeatMediaItem>();
            foreach (var image in scene.Illustrations)
                images.Add(image with
                {
                    Url = image.Status == "Ready" && !string.IsNullOrWhiteSpace(image.Url)
                    ? await PreviewUrlAsync(package, image.Url, ct, legacyPaths.Contains(image.Url)) : null
                });
            var audio = new List<AudioMediaItem>();
            foreach (var item in scene.Audio)
                audio.Add(item with
                {
                    Url = item.Status == "Ready" && !string.IsNullOrWhiteSpace(item.Url)
                    ? await PreviewUrlAsync(package, item.Url, ct, legacyPaths.Contains(item.Url)) : null
                });
            scenes.Add(scene with { Illustrations = images, Audio = audio });
        }
        return package with { Scenes = scenes };
    }

    private Task<string> PreviewUrlAsync(StoryMediaPackage package, string path, CancellationToken ct, bool legacy)
    {
        // Previously completed seeded packages may contain external HTTPS URLs, not storage keys.
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            if ((legacy || package.PackageRevision == 0 && package.IsConfirmed) && uri.Scheme == Uri.UriSchemeHttps)
                return Task.FromResult(path);
            throw new ConflictException("MEDIA_STORAGE_PATH_REQUIRED");
        }
        return _mediaStorage.GetSignedUrlAsync(path, TimeSpan.FromMinutes(10), ct);
    }

    public async Task<MediaGenerationProgress> RetryAsync(
        int userId, int storyId, CancellationToken cancellationToken = default)
    {
        await LoadAuthorizedStoryAsync(userId, storyId, cancellationToken);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, cancellationToken);
            var story = await FreshAsync<Story>(storyId, cancellationToken)
                        ?? throw new NotFoundException("Story", storyId);
            MediaStoryAccess.EnsureSupported(story);
            await EnsureMutationPermissionAsync(story, userId, cancellationToken);
            await CreatorPublicationGuard.EnsureWritableAsync(_unitOfWork, storyId, null, cancellationToken);
            if (story.Status != StoryStatus.MediaProcessing)
                throw new ConflictException("MEDIA_JOB_RETRY_NOT_ALLOWED");
            var job = (await _unitOfWork.Repository<StoryGenerationJob>().FindAsync(
                    x => x.StoryId == storyId && x.Operation == GenerationJobOperation.GenerateMediaPackage,
                    cancellationToken: cancellationToken))
                .OrderByDescending(x => x.Id)
                .FirstOrDefault()
                ?? throw new NotFoundException("Media generation job", storyId);

            if (job.Status != GenerationJobStatus.Failed)
                throw new ConflictException("MEDIA_JOB_NOT_FAILED");
            if (job.StoryVersionId is not int versionId)
                throw new ConflictException("INVALID_MEDIA_HANDOFF");

            var version = await FreshAsync<StoryVersion>(versionId, cancellationToken);
            if (version is null || version.StoryId != storyId || !version.IsCurrent || string.IsNullOrWhiteSpace(version.Content))
                throw new ConflictException("INVALID_MEDIA_HANDOFF");

            var assets = await _unitOfWork.Repository<MediaAsset>().FindAsync(
                x => x.StoryVersionId == versionId && (x.IsCurrent || x.ReplacementResolvedAt == null && x.ReplacesMediaAssetId != null) && x.Status != MediaStatus.Ready,
                cancellationToken: cancellationToken);
            foreach (var asset in assets)
            {
                asset.Status = MediaStatus.Queued;
                asset.ValidationStatus = ValidationStatus.Pending;
                asset.AttemptCount = 0;
                asset.LastValidationReason = null;
                asset.ValidationResultJson = null;
                asset.CompletedAt = null;
                _unitOfWork.Repository<MediaAsset>().Update(asset);
            }

            await ValidateParentContextAsync(story, versionId, cancellationToken);
            job.RequestedByUserId = userId;
            // Branches remain terminal until preparation queues them atomically with the parent context.
            job.MediaContextId = null;
            job.Status = GenerationJobStatus.Pending;
            job.Stage = JobStage.MediaPending;
            job.AttemptNo = 0;
            job.ErrorCode = null;
            job.FallbackMessage = null;
            job.LeaseExpiresAt = null;
            job.CompletedAt = null;
            job.StartedAt = _clock.GetUtcNow().UtcDateTime;
            _unitOfWork.Repository<StoryGenerationJob>().Update(job);
            // Attach the no-tracking entity before rotating the concurrency token so EF
            // keeps the database token as the original value in the UPDATE predicate.
            RotateToken(job);

            story.Status = StoryStatus.MediaProcessing;
            _unitOfWork.Repository<Story>().Update(story);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            _logger.LogInformation("Media retry queued Story={StoryId} Version={VersionId} Job={JobId}",
                storyId, versionId, job.Id);
        }
        catch
        {
            await RollbackMediaAsync(cancellationToken);
            throw;
        }

        return await GetProgressAsync(userId, storyId, cancellationToken);
    }

    public Task RegenerateIllustrationBeatAsync(int userId, int storyId, int beatId,
        RegenerateMediaRequest request, CancellationToken ct = default) =>
        RegenerateAsync(userId, storyId, beatId, MediaType.Illustration, request, ct);

    public Task RegenerateSegmentAudioAsync(int userId, int storyId, int segmentId,
        RegenerateMediaRequest request, CancellationToken ct = default) =>
        RegenerateAsync(userId, storyId, segmentId, MediaType.TtsAudio, request, ct);

    public async Task<MediaGenerationProgress> ConfirmAsync(int userId, int storyId,
        ConfirmMediaPackageRequest input, CancellationToken ct = default)
    {
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, ct);
            var story = await LoadAuthorizedStoryAsync(userId, storyId, ct);
            await EnsureMutationPermissionAsync(story, userId, ct, allowFinalizedReplay: true);
            var version = await _unitOfWork.Repository<StoryVersion>().FirstOrDefaultAsync(
                x => x.StoryId == storyId && x.IsCurrent && !x.IsDeleted, cancellationToken: ct)
                ?? throw new ConflictException("CURRENT_STORY_VERSION_REQUIRED");
            if (version.Id != input.StoryVersionId || input.PackageRevision <= 0 || version.MediaState.MediaPackageRevision != input.PackageRevision)
                throw new ConflictException("STALE_MEDIA_PACKAGE");
            await EnsureNoActiveJobAsync(storyId, ct);
            if (story.Status == StoryStatus.Ready && MediaPackageConfirmation.IsConfirmed(version))
            {
                await _unitOfWork.CommitTransactionAsync(ct);
                return await GetProgressAsync(userId, storyId, ct);
            }
            await EnsureMutationPermissionAsync(story, userId, ct);
            await CreatorPublicationGuard.EnsureWritableAsync(_unitOfWork, storyId, version.Id, ct);
            if (story.Status != StoryStatus.MediaReview) throw new ConflictException("MEDIA_PACKAGE_NOT_AWAITING_CONFIRMATION");
            var job = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x =>
                x.StoryId == storyId && x.StoryVersionId == version.Id &&
                x.Operation == GenerationJobOperation.GenerateMediaPackage && x.Status == GenerationJobStatus.Completed, cancellationToken: ct);
            if (job is null || !(await _readinessService.CheckAsync(version.Id, null, ct)).IsReady)
                throw new ConflictException("MEDIA_PACKAGE_INCOMPLETE");
            await EnsureNoPendingReplacementAsync(version.Id, ct);
            GrayZoneGate.EnsureCanContinue(story, version);
            await ValidateParentContextAsync(story, version.Id, ct);
            version.MediaState.ConfirmedMediaPackageRevision = version.MediaState.MediaPackageRevision;
            version.MediaState.MediaConfirmedByUserId = userId;
            version.MediaState.MediaConfirmedAt = _clock.GetUtcNow().UtcDateTime;
            _unitOfWork.Repository<StoryVersion>().Update(version);
            story.Status = StoryStatus.Ready;
            _unitOfWork.Repository<Story>().Update(story);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch { await RollbackMediaAsync(CancellationToken.None); throw; }
        return await GetProgressAsync(userId, storyId, ct);
    }

    private async Task EnsureNoActiveJobAsync(int storyId, CancellationToken ct)
    {
        if (await _unitOfWork.Repository<StoryGenerationJob>().ExistsAsync(x => x.StoryId == storyId &&
            (x.Status == GenerationJobStatus.Pending || x.Status == GenerationJobStatus.Processing), ct))
            throw new ConflictException("MEDIA_JOB_ACTIVE");
    }

    private async Task EnsureMutationPermissionAsync(Story story, int userId, CancellationToken ct, bool allowFinalizedReplay = false)
    {
        if (await MediaStoryAccess.AuthorizeCreatorAsync(_unitOfWork, story, userId, ct)) return;
        if (_supervision is null || !story.ChildProfileId.HasValue) throw new ForbiddenException();
        var user = await _unitOfWork.Repository<UserAccount>().FirstOrDefaultAsync(x => x.Id == userId, cancellationToken: ct);
        if (user is null || user.IsDeleted || user.Role != UserRole.Parent || user.Status != AccountStatus.Active || user.LockedUntil > _clock.GetUtcNow().UtcDateTime)
            throw new ForbiddenException();
        await _supervision.EnsurePermissionAsync(story.ChildProfileId.Value, userId, Permission.ApproveStory, ct);
        var child = await _unitOfWork.Repository<ChildProfile>().FirstOrDefaultAsync(x =>
            x.Id == story.ChildProfileId && !x.IsDeleted, cancellationToken: ct);
        if (child is null || child.Status != ChildProfileStatus.Active) throw new ConflictException("CHILD_NOT_ACTIVE");
        if (ParentStoryContext.IsEditorStory(story))
        {
            var policy = new ParentManualAuthoringPolicy(_unitOfWork, _supervision);
            await policy.AuthorizeAsync(userId, story, ct);
            if (!allowFinalizedReplay && await policy.IsLockedAsync(story.Id, ct)) throw new ConflictException("PARENT_STORY_FINALIZED");
        }
    }

    private async Task ValidateParentContextAsync(Story story, int versionId, CancellationToken ct)
    {
        if (story.Scope != StoryScope.Parent) return;
        var policy = await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(x =>
            x.ChildProfileId == story.ChildProfileId && !x.IsDeleted, cancellationToken: ct);
        if (policy is null || !policy.ConsentRecorded || !policy.ConsentRecordedAt.HasValue ||
            ConsentPolicy.IsOutdated(policy.ConsentPolicyVersion)) throw new ForbiddenException("CONSENT_REQUIRED");
        if (!ParentStoryContext.IsEditorStory(story)) return;
        if (_supervision is null) throw new ForbiddenException();
        var context = (await _unitOfWork.Repository<MediaContext>().FindAsync(x => x.StoryVersionId == versionId, cancellationToken: ct))
            .OrderByDescending(x => x.Revision).FirstOrDefault() ?? throw new ConflictException("MEDIA_CONTEXT_REQUIRED");
        using var json = JsonDocument.Parse(context.ContextJson);
        var snapshot = json.RootElement.GetProperty("storyFacts").GetProperty("acceptedContext").GetRawText();
        await new ParentAssistanceAccessPolicy(_unitOfWork, _supervision).ValidateContextAsync(story, snapshot, ct);
    }

    public async Task<MediaJobProcessResult> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        await ReconcileImageBranchesAsync(cancellationToken);
        if (await TryFinalizeNextAsync(cancellationToken)) return MediaJobProcessResult.Completed;
        var now = _clock.GetUtcNow().UtcDateTime;
        var job = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(
            x => x.Operation == GenerationJobOperation.GenerateMediaPackage && x.ParentJobId == null &&
                 (x.NextAttemptAt == null || x.NextAttemptAt <= now) &&
                 (x.Status == GenerationJobStatus.Pending ||
                  (x.Status == GenerationJobStatus.Processing && x.MediaContextId == null && x.LeaseExpiresAt < now)),
            cancellationToken: cancellationToken);
        if (job is null) return MediaJobProcessResult.NoJob;

        var preClaimToken = job.ConcurrencyToken;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(job.StoryId, cancellationToken);
            var revalidatedJob = await FreshAsync<StoryGenerationJob>(job.Id, cancellationToken);
            if (revalidatedJob is null || revalidatedJob.ConcurrencyToken != job.ConcurrencyToken)
                throw new InvalidOperationException("CONCURRENT_CLAIM_DETECTED");
            if (job.AttemptNo >= job.MaxAttempts) throw new PermanentMediaGenerationException("MEDIA_PREPARATION_RETRY_EXHAUSTED");
            var story = await FreshAsync<Story>(job.StoryId, cancellationToken)
                        ?? throw new InvalidOperationException("STORY_NOT_FOUND");
            if (story.Status is not (StoryStatus.Approved or StoryStatus.MediaProcessing))
                throw new InvalidOperationException("STALE_MEDIA_HANDOFF");
            MediaStoryAccess.EnsureSupported(story);
            await CreatorPublicationGuard.EnsureWritableAsync(_unitOfWork, story.Id, job.StoryVersionId, cancellationToken);
            if (story.Scope == StoryScope.Creator)
                await MediaStoryAccess.AuthorizeCreatorAsync(_unitOfWork, story, job.RequestedByUserId ?? story.AuthorUserId, cancellationToken);
            var existingActiveJob = (await _unitOfWork.Repository<StoryGenerationJob>().FindAsync(
                x => x.StoryId == story.Id &&
                     x.Operation == GenerationJobOperation.GenerateMediaPackage &&
                     (x.Status == GenerationJobStatus.Processing || x.Status == GenerationJobStatus.Pending) &&
                     x.Id != job.Id,
                cancellationToken: cancellationToken)).FirstOrDefault();
            if (existingActiveJob is not null)
                throw new InvalidOperationException("DUPLICATE_ACTIVE_JOB");
            if (job.StoryVersionId is int claimVersionId)
            {
                var claimVersion = await FreshAsync<StoryVersion>(claimVersionId, cancellationToken)
                    ?? throw new InvalidOperationException("INVALID_MEDIA_HANDOFF");
                if (claimVersion.MediaState.MediaPackageRevision == 0)
                {
                    MediaPackageConfirmation.Invalidate(claimVersion);
                    _unitOfWork.Repository<StoryVersion>().Update(claimVersion);
                }
                job.MediaPackageRevision = claimVersion.MediaState.MediaPackageRevision;
            }
            story.Status = StoryStatus.MediaProcessing;
            _unitOfWork.Repository<Story>().Update(story);
            job.Status = GenerationJobStatus.Processing;
            job.Stage = JobStage.MediaContextBuilding;
            job.AttemptNo++;
            job.StartedAt = now;
            job.CompletedAt = null;
            job.ErrorCode = null;
            job.FallbackMessage = null;
            job.LeaseExpiresAt = now.Add(_jobLease);
            RotateToken(job);
            _unitOfWork.Repository<StoryGenerationJob>().Update(job);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            await RollbackMediaAsync(cancellationToken);
            if (exception.Message == "CONCURRENT_CLAIM_DETECTED") return MediaJobProcessResult.NoJob;
            var errorCode = PublicErrorCode(exception);
            var isPermanent = IsPermanentFailure(exception);
            await _failureFinalizer.MarkFailedAsync(job.Id, preClaimToken, errorCode, CancellationToken.None);
            return MediaJobProcessResult.Failed(isPermanent, errorCode);
        }

        var claimedToken = job.ConcurrencyToken;
        try
        {
            await ProcessClaimedAsync(job.Id, claimedToken, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            var errorCode = PublicErrorCode(exception);
            var isPermanent = IsPermanentFailure(exception);
            _logger.LogError(exception, "Phase 5 job {JobId} failed with {ErrorCode}", job.Id, errorCode);
            await _failureFinalizer.MarkFailedAsync(
                job.Id, claimedToken, errorCode, CancellationToken.None);
            return MediaJobProcessResult.Failed(isPermanent, errorCode);
        }
        return MediaJobProcessResult.Completed;
    }

    private async Task ProcessClaimedAsync(int jobId, string claimedToken, CancellationToken cancellationToken)
    {
        var state = await LoadStateAsync(jobId, claimedToken, cancellationToken);
        var mediaContext = await GetOrCreateContextAsync(state, cancellationToken);
        var scenes = await GetOrCreateScenesAsync(state, claimedToken, mediaContext, cancellationToken);
        await UpdateStageAsync(state.Job.Id, claimedToken, JobStage.MediaGenerating, cancellationToken);

        foreach (var scene in scenes.OrderBy(x => x.SceneIndex))
        {
            await GetOrCreateBeatsAsync(jobId, claimedToken, state.Version.Id, mediaContext, scene, cancellationToken);
            await PrepareSegmentsAsync(scene, cancellationToken);
        }
        await QueueBranchesAsync(state, mediaContext, claimedToken, cancellationToken);
    }

    private async Task<MediaContext> GetOrCreateContextAsync(MediaState state, CancellationToken cancellationToken)
    {
        var existing = (await _unitOfWork.Repository<MediaContext>().FindAsync(
                x => x.StoryVersionId == state.Version.Id, cancellationToken: cancellationToken))
            .OrderByDescending(x => x.Revision).FirstOrDefault();
        if (existing is not null) return existing;

        StoryGenerationRequest? request = null;
        if (state.Job.GenerationRequestId.HasValue)
            request = await FreshAsync<StoryGenerationRequest>(state.Job.GenerationRequestId.Value, cancellationToken);
        var context = new MediaContext
        {
            StoryVersionId = state.Version.Id,
            Revision = 1,
            ContextJson = _contextBuilder.Build(new MediaContextBuildRequest(
                state.Version.Id, state.Version.Title, state.Version.Content!, state.Version.Lesson,
                state.Story.Source == StorySource.Ai ? state.Version.OutlineOpening : null,
                state.Story.Source == StorySource.Ai ? state.Version.OutlineDevelopment : null,
                state.Story.Source == StorySource.Ai ? state.Version.OutlineEnding : null,
                request?.AcceptedInputJson, request?.ContextSnapshotJson)
                )
        };
        await AssertHandoffFreshAsync(state.Job.Id, state.Job.ConcurrencyToken, state.Version.Id, cancellationToken);
        await _unitOfWork.Repository<MediaContext>().AddAsync(context, cancellationToken);
        await AssertHandoffFreshAsync(state.Job.Id, state.Job.ConcurrencyToken, state.Version.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return context;
    }

    private async Task<IReadOnlyList<StoryScene>> GetOrCreateScenesAsync(
        MediaState state, string claimedToken, MediaContext mediaContext, CancellationToken cancellationToken)
    {
        var existing = await _unitOfWork.Repository<StoryScene>().FindAsync(
            x => x.StoryVersionId == state.Version.Id, cancellationToken: cancellationToken);
        if (existing.Count > 0)
        {
            ValidatePersistedScenes(state.Version.Content!, existing);
            return existing;
        }

        await AssertFreshAsync(state.Job.Id, claimedToken, state.Version.Id, mediaContext.Id, cancellationToken);
        await UpdateStageAsync(state.Job.Id, claimedToken, JobStage.MediaSegmenting, cancellationToken);
        var blocks = _blockParser.Parse(state.Version.Content!);
        var selections = await _segmenter.SegmentAsync(
            new SceneSegmentationRequest(state.Version.Content!, blocks, mediaContext.ContextJson), cancellationToken);
        var validated = _coverageValidator.ValidateAndAssemble(state.Version.Content!, blocks, selections);
        await AssertFreshAsync(state.Job.Id, claimedToken, state.Version.Id, mediaContext.Id, cancellationToken);
        var scenes = validated.Select(x => new StoryScene
        {
            StoryVersionId = state.Version.Id,
            SceneIndex = x.SceneIndex,
            TextRangeStart = x.TextRangeStart,
            TextRangeEnd = x.TextRangeEnd,
            SceneText = x.SceneText,
            VisualDescription = x.VisualDescription
        }).ToArray();
        await _unitOfWork.Repository<StoryScene>().AddRangeAsync(scenes, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return scenes;
    }

    private async Task<IReadOnlyList<IllustrationBeat>> GetOrCreateBeatsAsync(
        int jobId, string token, int versionId, MediaContext mediaContext, StoryScene scene,
        CancellationToken cancellationToken)
    {
        var existing = await _unitOfWork.Repository<IllustrationBeat>().FindAsync(
            x => x.StorySceneId == scene.Id, cancellationToken: cancellationToken);
        if (existing.Count > 0)
        {
            ValidateBeats(scene, existing.Select(x => new IllustrationBeatSelection(
                x.BeatOrder, x.StartOffset, x.EndOffset, x.VisualFocus)).ToArray());
            return existing;
        }

        var legacyAsset = await _unitOfWork.Repository<MediaAsset>().FirstOrDefaultAsync(
            x => x.IsCurrent && x.StorySceneId == scene.Id && x.Type == MediaType.Illustration,
            cancellationToken: cancellationToken);
        IReadOnlyList<IllustrationBeatSelection> selections = [];
        if (legacyAsset is null)
        {
            await UpdateStageAsync(jobId, token, JobStage.MediaSegmenting, cancellationToken);
            try
            {
                selections = await _beatPlanner.PlanAsync(new IllustrationBeatPlanRequest(
                    scene.SceneText, scene.VisualDescription, mediaContext.ContextJson), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { /* Planner unavailable: use one whole-scene beat below. */ }
        }
        if (!AreValidBeats(scene, selections))
        {
            _logger.LogWarning("Illustration beat plan fallback StoryVersion={VersionId} Scene={SceneId}",
                versionId, scene.Id);
            selections = [new IllustrationBeatSelection(1, 0, scene.SceneText.Length,
                scene.VisualDescription ?? "Illustrate the scene's main moment")];
        }
        await AssertFreshAsync(jobId, token, versionId, mediaContext.Id, cancellationToken);
        var beats = selections.Select(x => new IllustrationBeat
        {
            StorySceneId = scene.Id,
            BeatOrder = x.BeatOrder,
            StartOffset = x.StartOffset,
            EndOffset = x.EndOffset,
            VisualFocus = x.VisualFocus.Trim()
        }).ToArray();
        await _unitOfWork.Repository<IllustrationBeat>().AddRangeAsync(beats, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (legacyAsset is not null)
        {
            legacyAsset.IllustrationBeatId = beats[0].Id;
            _unitOfWork.Repository<MediaAsset>().Update(legacyAsset);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return beats;
    }

    private static bool AreValidBeats(StoryScene scene, IReadOnlyList<IllustrationBeatSelection> beats) =>
        beats.Count is >= 1 and <= 3 && beats.OrderBy(x => x.BeatOrder)
            .Select((beat, index) => beat.BeatOrder == index + 1 &&
                beat.StartOffset >= 0 && beat.EndOffset <= scene.SceneText.Length &&
                beat.EndOffset > beat.StartOffset && !string.IsNullOrWhiteSpace(beat.VisualFocus) &&
                beat.VisualFocus.Length <= 500 &&
                scene.SceneText[beat.StartOffset..beat.EndOffset].Any(c => !char.IsWhiteSpace(c)))
            .All(x => x) && beats.Select(x => (x.StartOffset, x.EndOffset, x.VisualFocus.Trim()))
            .Distinct().Count() == beats.Count &&
        beats.OrderBy(x => x.BeatOrder).Select(x => x.StartOffset)
            .SequenceEqual(beats.Select(x => x.StartOffset).OrderBy(x => x));

    private static void ValidateBeats(StoryScene scene, IReadOnlyList<IllustrationBeatSelection> beats)
    {
        if (!AreValidBeats(scene, beats)) throw new InvalidOperationException("INVALID_PERSISTED_ILLUSTRATION_BEATS");
    }

    private async Task EnsureIllustrationAsync(
        int jobId, string claimedToken, int storyId, int versionId, MediaContext mediaContext,
        StoryScene scene, IllustrationBeat beat, SceneSpecification baseSpecification, CancellationToken cancellationToken)
    {
        var asset = await GetOrCreateIllustrationAssetAsync(versionId, scene, beat, cancellationToken);
        if (asset.Status == MediaStatus.Ready) return;

        // Failure-feedback history for this asset (reset when retrying).
        var failureHistory = new List<(string Code, string Reason)>();
        if (!string.IsNullOrWhiteSpace(asset.LastValidationReason)) failureHistory.Add(("PREVIOUS_VALIDATION_FAILED", asset.LastValidationReason));

        if (asset.AttemptCount >= _assetMaxAttempts) throw new InvalidOperationException("ILLUSTRATION_RETRY_EXHAUSTED");
        var attempt = asset.AttemptCount;
        try
        {
            await AssertFreshAsync(jobId, claimedToken, versionId, mediaContext.Id, cancellationToken);
            await StartAssetAttemptAsync(jobId, claimedToken, mediaContext.Id, asset, cancellationToken);

            // Build specification with corrective feedback from previous attempts.
            var specification = BuildSpecificationWithFeedback(baseSpecification, failureHistory);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.Images.ProviderTimeoutSeconds, 1, 600)));
            var illustration = await MeasureMediaStageAsync("ImageGeneration", jobId, asset,
                () => _imageProvider.GenerateAsync(specification, timeout.Token));
            timeout.Token.ThrowIfCancellationRequested();
            Task<MediaEvaluationResult> Alignment() => MeasureMediaStageAsync("ImageAlignment", jobId, asset,
                () => _alignmentEvaluator.EvaluateAsync(specification, illustration, timeout.Token));
            Task<MediaEvaluationResult> Safety() => MeasureMediaStageAsync("ImageSafety", jobId, asset,
                () => _safetyEvaluator.EvaluateAsync(specification, illustration, timeout.Token));
            MediaEvaluationResult alignment, safety;
            if (_options.ParallelImageEvaluation)
            {
                var alignmentTask = Alignment();
                var safetyTask = Safety();
                await Task.WhenAll(alignmentTask, safetyTask);
                alignment = await alignmentTask;
                safety = await safetyTask;
            }
            else
            {
                alignment = await Alignment();
                safety = await Safety();
            }

            var evalResult = BuildValidationResultJson(alignment, safety);
            asset.ValidationResultJson = evalResult;

            if (!alignment.Passed)
            {
                if (alignment.Reason == "MEDIA_EVALUATOR_NOT_CONFIGURED")
                    throw new PermanentMediaGenerationException("MEDIA_EVALUATOR_NOT_CONFIGURED");
                failureHistory.Add(("IMAGE_ALIGNMENT_FAILED", alignment.Reason ?? "Alignment check failed"));
                asset.LastValidationReason = alignment.Reason;
                asset.ValidationStatus = ValidationStatus.Failed;
                throw new InvalidOperationException("IMAGE_ALIGNMENT_FAILED");
            }
            if (!safety.Passed)
            {
                if (safety.Reason == "MEDIA_EVALUATOR_NOT_CONFIGURED")
                    throw new PermanentMediaGenerationException("MEDIA_EVALUATOR_NOT_CONFIGURED");
                failureHistory.Add(("IMAGE_SAFETY_FAILED", safety.Reason ?? "Safety check failed"));
                asset.LastValidationReason = safety.Reason;
                asset.ValidationStatus = ValidationStatus.Failed;
                throw new InvalidOperationException("IMAGE_SAFETY_FAILED");
            }

            await AssertFreshAsync(jobId, claimedToken, versionId, mediaContext.Id, cancellationToken);
            var storagePath = $"{storyId}/versions/{versionId}/v{mediaContext.Revision}/scene-{scene.SceneIndex}-beat-{beat.BeatOrder}-asset-{asset.Id}-{Guid.NewGuid():N}-a{attempt + 1}{illustration.SuggestedExtension}";
            await using var content = illustration.OpenReadStream();
            asset.Url = await MeasureMediaStageAsync("ImageUpload", jobId, asset,
                () => _mediaStorage.UploadAsync(storagePath, content, illustration.MimeType, cancellationToken));
            asset.MimeType = illustration.MimeType;
            asset.Provider = illustration.GetMetadata("provider") ?? "Gemini";
            asset.Model = illustration.GetMetadata("model") ?? "unknown";
            asset.Status = MediaStatus.Ready;
            asset.ValidationStatus = ValidationStatus.Passed;
            asset.CompletedAt = _clock.GetUtcNow().UtcDateTime;
            await PersistReadyAssetAsync(jobId, claimedToken, mediaContext.Id, asset, cancellationToken);
            _logger.LogInformation(
                "Illustration ready Story={StoryId} Version={VersionId} Scene={SceneId} Beat={BeatId} Asset={AssetId} Attempt={Attempt}",
                storyId, versionId, scene.Id, beat.Id, asset.Id, asset.AttemptCount);
            return;
        }
        catch (PermanentMediaGenerationException) when (!cancellationToken.IsCancellationRequested)
        {
            // Permanent failure — record but break out of retry loop and rethrow.
            await RecordAssetFailureAsync(jobId, claimedToken, mediaContext.Id, asset, cancellationToken);
            throw;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && !IsStale(exception))
        {
            await RecordAssetFailureAsync(jobId, claimedToken, mediaContext.Id, asset, cancellationToken);
            throw;
        }

    }

    private async Task PrepareSegmentsAsync(StoryScene scene, CancellationToken cancellationToken)
    {
        // Get or create StorySegments for this scene.
        var segments = _segmentService.CreateSegmentsForScene(scene);
        if (segments.Count == 0) return;

        // Persist segments that don't exist yet.
        foreach (var segment in segments)
        {
            var existing = await _unitOfWork.Repository<StorySegment>().FirstOrDefaultAsync(
                x => x.StorySceneId == scene.Id && x.SegmentOrder == segment.SegmentOrder,
                cancellationToken: cancellationToken);
            if (existing is null)
            {
                await _unitOfWork.Repository<StorySegment>().AddAsync(segment, cancellationToken);
            }
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);

    }

    private async Task EnsureSegmentAudioAsync(
        int jobId, string claimedToken, int storyId, int versionId, MediaContext mediaContext,
        StoryScene scene, StorySegment segment, CancellationToken cancellationToken)
    {
        var asset = await GetOrCreateSegmentAudioAssetAsync(versionId, segment, cancellationToken);
        if (asset.Status == MediaStatus.Ready) return;

        var failureHistory = new List<(string Code, string Reason)>();

        if (asset.AttemptCount >= _assetMaxAttempts) throw new InvalidOperationException("TTS_SEGMENT_RETRY_EXHAUSTED");
        var attempt = asset.AttemptCount;
        try
        {
            await AssertFreshAsync(jobId, claimedToken, versionId, mediaContext.Id, cancellationToken);
            await StartAssetAttemptAsync(jobId, claimedToken, mediaContext.Id, asset, cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.Audio.ProviderTimeoutSeconds, 1, 600)));
            var audio = await MeasureMediaStageAsync("TtsGeneration", jobId, asset,
                () => _ttsProvider.GenerateAsync(segment.TextContent, timeout.Token));
            timeout.Token.ThrowIfCancellationRequested();

            // Structural quality gate — magic bytes + word timings check.
            var tokens = SsmlTokenizer.Tokenize(segment.TextContent);
            var gateResult = _audioQualityGate.Validate(audio, tokens);
            if (!gateResult.IsPass)
            {
                var reason = gateResult.Reason ?? "AUDIO_QUALITY_GATE_FAILED";
                asset.LastValidationReason = reason;
                asset.ValidationStatus = ValidationStatus.Failed;
                failureHistory.Add(("AUDIO_QUALITY_GATE_FAILED", reason));

                if (IsDeterministicAudioFailure(reason))
                {
                    asset.Status = MediaStatus.ManualReview;
                    throw new InvalidOperationException($"AUDIO_QUALITY_GATE_DETERMINISTIC_FAILURE:{reason}");
                }

                asset.Status = MediaStatus.Failed;
                throw new InvalidOperationException("AUDIO_QUALITY_GATE_FAILED");
            }

            await AssertFreshAsync(jobId, claimedToken, versionId, mediaContext.Id, cancellationToken);
            var storagePath = $"{storyId}/versions/{versionId}/v{mediaContext.Revision}/audio-s{scene.SceneIndex}-{segment.SegmentOrder}-asset-{asset.Id}-{Guid.NewGuid():N}-a{attempt + 1}{audio.SuggestedExtension}";
            await using var content = audio.OpenReadStream();
            asset.Url = await MeasureMediaStageAsync("AudioUpload", jobId, asset,
                () => _mediaStorage.UploadAsync(storagePath, content, audio.MimeType, cancellationToken));
            asset.MimeType = audio.MimeType;
            asset.WordTimings = audio.GetMetadata("wordTimingsJson");
            asset.Provider = audio.GetMetadata("provider") ?? "GoogleCloud";
            asset.Model = audio.GetMetadata("model") ?? "unknown";
            asset.Status = MediaStatus.Ready;
            asset.ValidationStatus = ValidationStatus.Passed;
            asset.CompletedAt = _clock.GetUtcNow().UtcDateTime;
            await PersistReadyAssetAsync(jobId, claimedToken, mediaContext.Id, asset, cancellationToken);
            _logger.LogInformation(
                "Audio ready Story={StoryId} Version={VersionId} Scene={SceneId} Segment={SegmentId} Asset={AssetId} Attempt={Attempt}",
                storyId, versionId, scene.Id, segment.Id, asset.Id, asset.AttemptCount);
            return;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && !IsStale(exception))
        {
            await RecordAssetFailureAsync(jobId, claimedToken, mediaContext.Id, asset, cancellationToken);
            throw;
        }

    }

    private static bool IsDeterministicAudioFailure(string reason) =>
        reason.StartsWith("AUDIO_MAGIC_BYTES_INVALID", StringComparison.OrdinalIgnoreCase) ||
        reason.Equals("AUDIO_CONTENT_EMPTY", StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("UNRECOGNIZED_AUDIO_HEADER", StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("CONTENT_TOO_SHORT", StringComparison.OrdinalIgnoreCase);

    private async Task<MediaAsset> GetOrCreateIllustrationAssetAsync(
        int versionId, StoryScene scene, IllustrationBeat beat, CancellationToken cancellationToken)
    {
        var existing = await _unitOfWork.Repository<MediaAsset>().FirstOrDefaultAsync(
            x => x.IllustrationBeatId == beat.Id && x.Type == MediaType.Illustration && x.IsCurrent,
            cancellationToken: cancellationToken);
        if (existing is not null)
        {
            if (existing.StorySceneId != scene.Id || existing.StoryVersionId != versionId)
                throw new InvalidOperationException("INVALID_ILLUSTRATION_BEAT_ASSET");
            return await ReplacementOrCurrentAsync(existing, cancellationToken);
        }
        var asset = new MediaAsset
        {
            StoryVersionId = versionId,
            RequestedPackageRevision = (await FreshAsync<StoryVersion>(versionId, cancellationToken))!.MediaState.MediaPackageRevision,
            StorySceneId = scene.Id,
            IllustrationBeatId = beat.Id,
            SceneIndex = scene.SceneIndex,
            Type = MediaType.Illustration,
            Status = MediaStatus.Queued,
            ValidationStatus = ValidationStatus.Pending
        };
        await _unitOfWork.Repository<MediaAsset>().AddAsync(asset, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return asset;
    }

    private async Task<MediaAsset> GetOrCreateSegmentAudioAssetAsync(
        int versionId, StorySegment segment, CancellationToken cancellationToken)
    {
        var existing = await _unitOfWork.Repository<MediaAsset>().FirstOrDefaultAsync(
            x => x.StorySegmentId == segment.Id && x.Type == MediaType.TtsAudio && x.IsCurrent,
            cancellationToken: cancellationToken);
        if (existing is not null) return await ReplacementOrCurrentAsync(existing, cancellationToken);
        var asset = new MediaAsset
        {
            StoryVersionId = versionId,
            RequestedPackageRevision = (await FreshAsync<StoryVersion>(versionId, cancellationToken))!.MediaState.MediaPackageRevision,
            StorySceneId = segment.StorySceneId,
            StorySegmentId = segment.Id,
            SceneIndex = null,
            Type = MediaType.TtsAudio,
            Status = MediaStatus.Queued,
            ValidationStatus = ValidationStatus.Pending
        };
        await _unitOfWork.Repository<MediaAsset>().AddAsync(asset, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return asset;
    }

    private async Task FinalizeAsync(
        int jobId, string token, int versionId, int mediaContextId, int sceneCount,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync((await RequireClaimedJobAsync(jobId, token, cancellationToken)).StoryId, cancellationToken);
            var job = await RequireClaimedJobAsync(jobId, token, cancellationToken);
            await AssertFreshAsync(jobId, token, versionId, mediaContextId, cancellationToken);
            var story = await FreshAsync<Story>(job.StoryId, cancellationToken)
                ?? throw new InvalidOperationException("STORY_NOT_FOUND");
            var readiness = await _readinessService.CheckAsync(versionId, sceneCount, cancellationToken);
            if (!readiness.IsReady)
            {
                _logger.LogWarning("Media package incomplete for version {VersionId}: {Readiness}", versionId, JsonSerializer.Serialize(readiness));
                throw new InvalidOperationException("MEDIA_PACKAGE_INCOMPLETE");
            }
            story.Status = StoryStatus.MediaReview;
            _unitOfWork.Repository<Story>().Update(story);
            job.Status = GenerationJobStatus.Completed;
            job.Stage = JobStage.MediaCompleted;
            job.CompletedAt = _clock.GetUtcNow().UtcDateTime;
            job.LeaseExpiresAt = null;
            _unitOfWork.Repository<StoryGenerationJob>().Update(job);
            RotateToken(job);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch { await RollbackMediaAsync(CancellationToken.None); throw; }
    }

    private static SceneSpecification BuildSpecificationWithFeedback(
        SceneSpecification baseSpec, IReadOnlyList<(string Code, string Reason)> failureHistory)
    {
        if (failureHistory.Count == 0) return baseSpec;
        var sb = new StringBuilder();
        sb.AppendLine("IMPORTANT: Previous generation attempts had the following issues. Please correct them in this attempt:");
        foreach (var (code, reason) in failureHistory)
        {
            sb.AppendLine($"- [{code}] {reason}");
        }
        sb.AppendLine();
        sb.AppendLine("STRICT requirements: Apply the corrections above and do not repeat the same mistakes.");
        return baseSpec.WithFeedback(sb.ToString());
    }

    private string BuildValidationResultJson(MediaEvaluationResult alignment, MediaEvaluationResult safety)
    {
        return JsonSerializer.Serialize(new
        {
            alignmentScore = alignment.Decision.ToString(),
            alignmentReason = alignment.Reason,
            safetyScore = safety.Decision.ToString(),
            safetyReason = safety.Reason,
            evaluatedAt = _clock.GetUtcNow().UtcDateTime
        });
    }

    private async Task AssertFreshAsync(
        int jobId, string token, int versionId, int contextId, CancellationToken cancellationToken)
    {
        var job = await RequireClaimedJobAsync(jobId, token, cancellationToken);
        await AssertImageTaskClaimAsync(jobId, cancellationToken);
        await CreatorPublicationGuard.EnsureWritableAsync(_unitOfWork, job.StoryId, versionId, cancellationToken);
        var story = await FreshAsync<Story>(job.StoryId, cancellationToken);
        var version = await FreshAsync<StoryVersion>(versionId, cancellationToken);
        var latestContext = (await _unitOfWork.Repository<MediaContext>().FindAsync(
                x => x.StoryVersionId == versionId, cancellationToken: cancellationToken))
            .OrderByDescending(x => x.Revision).FirstOrDefault();
        if (story?.Status != StoryStatus.MediaProcessing || version is null || !version.IsCurrent ||
            version.StoryId != job.StoryId || job.StoryVersionId != versionId || latestContext is null || latestContext.Id != contextId)
            throw new InvalidOperationException("STALE_MEDIA_RESULT");
        if (job.MediaPackageRevision.HasValue && job.MediaPackageRevision != version.MediaState.MediaPackageRevision ||
            job.MediaContextId.HasValue && job.MediaContextId != contextId)
            throw new InvalidOperationException("STALE_MEDIA_RESULT");
        if (job.ParentJobId is int parentId)
        {
            var parent = await FreshAsync<StoryGenerationJob>(parentId, cancellationToken);
            if (parent?.Status != GenerationJobStatus.Processing || parent.StoryId != job.StoryId || parent.StoryVersionId != versionId ||
                parent.MediaPackageRevision != job.MediaPackageRevision) throw new InvalidOperationException("STALE_MEDIA_RESULT");
        }
        await EnsureMutationPermissionAsync(story!, job.RequestedByUserId ?? story!.AuthorUserId, cancellationToken);
        if (story!.Scope == StoryScope.Parent) await ValidateParentContextAsync(story, versionId, cancellationToken);
        GrayZoneGate.EnsureCanContinue(story!, version!);
    }

    private async Task AssertHandoffFreshAsync(
        int jobId, string token, int versionId, CancellationToken cancellationToken)
    {
        var job = await RequireClaimedJobAsync(jobId, token, cancellationToken);
        var story = await FreshAsync<Story>(job.StoryId, cancellationToken);
        var version = await FreshAsync<StoryVersion>(versionId, cancellationToken);
        if (story?.Status != StoryStatus.MediaProcessing || version is null || !version.IsCurrent ||
            job.StoryVersionId != versionId)
            throw new InvalidOperationException("STALE_MEDIA_RESULT");
    }

    private async Task<MediaState> LoadStateAsync(int jobId, string token, CancellationToken cancellationToken)
    {
        var job = await RequireClaimedJobAsync(jobId, token, cancellationToken);
        if (!job.StoryVersionId.HasValue) throw new InvalidOperationException("INVALID_MEDIA_HANDOFF");
        var version = await FreshAsync<StoryVersion>(job.StoryVersionId.Value, cancellationToken);
        if (version is null || version.StoryId != job.StoryId || !version.IsCurrent || string.IsNullOrEmpty(version.Content))
            throw new InvalidOperationException("INVALID_MEDIA_HANDOFF");
        var story = await FreshAsync<Story>(job.StoryId, cancellationToken)
                    ?? throw new InvalidOperationException("STORY_NOT_FOUND");
        if (story.Source == StorySource.Manual && job.GenerationRequestId.HasValue)
            throw new InvalidOperationException("INVALID_MEDIA_HANDOFF");

        return new MediaState(job, version, story);
    }

    private async Task<StoryGenerationJob> RequireClaimedJobAsync(
        int jobId, string token, CancellationToken cancellationToken)
    {
        var job = await FreshAsync<StoryGenerationJob>(jobId, cancellationToken);
        if (job is null || job.Status != GenerationJobStatus.Processing || job.ConcurrencyToken != token || job.LeaseExpiresAt.HasValue && job.LeaseExpiresAt <= _clock.GetUtcNow().UtcDateTime)
            throw new InvalidOperationException("STALE_JOB_RESULT");
        return job;
    }

    private async Task UpdateStageAsync(
        int jobId, string claimedToken, JobStage stage, CancellationToken cancellationToken)
    {
        var job = await RequireClaimedJobAsync(jobId, claimedToken, cancellationToken);
        job.Stage = stage;
        job.LeaseExpiresAt = _clock.GetUtcNow().UtcDateTime.Add(_jobLease);
        _unitOfWork.Repository<StoryGenerationJob>().Update(job);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static void ValidatePersistedScenes(string content, IReadOnlyList<StoryScene> scenes)
    {
        var ordered = scenes.OrderBy(x => x.SceneIndex).ToArray();
        var contentPosition = 0;
        for (var i = 0; i < ordered.Length; i++)
        {
            var scene = ordered[i];
            if (scene.SceneIndex != i || scene.TextRangeStart != contentPosition || scene.TextRangeEnd > content.Length ||
                scene.TextRangeEnd <= scene.TextRangeStart ||
                content[scene.TextRangeStart..scene.TextRangeEnd] != scene.SceneText)
                throw new InvalidOperationException("INVALID_PERSISTED_SCENE_COVERAGE");
            contentPosition = scene.TextRangeEnd;
        }
        if (contentPosition != content.Length) throw new InvalidOperationException("INVALID_PERSISTED_SCENE_COVERAGE");
    }

    private async Task<Story> LoadAuthorizedStoryAsync(int userId, int storyId, CancellationToken cancellationToken)
    {
        var story = await FreshAsync<Story>(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story", storyId);
        MediaStoryAccess.EnsureSupported(story);
        if (await MediaStoryAccess.AuthorizeCreatorAsync(_unitOfWork, story, userId, cancellationToken)) return story;
        if (ParentStoryContext.IsEditorStory(story))
        {
            if (_supervision is null) throw new ForbiddenException();
            await new ParentManualAuthoringPolicy(_unitOfWork, _supervision).AuthorizeAsync(userId, story, cancellationToken);
            return story;
        }
        if (story.AuthorUserId == userId) return story;
        var relationship = await _unitOfWork.Repository<SupervisionRelationship>().FirstOrDefaultAsync(
            x => x.ChildProfileId == story.ChildProfileId && x.SupervisorUserId == userId && x.RevokedAt == null,
            cancellationToken: cancellationToken);
        if (relationship is null) throw new ForbiddenException();
        return story;
    }

    private static string PublicErrorCode(Exception exception)
    {
        if (exception is TransientMediaGenerationException transient) return transient.ErrorCode;
        var permanent = FindPermanentException(exception);
        if (permanent is not null) return permanent.ErrorCode;
        var unwrapped = UnwrapException(exception);
        return unwrapped.Message switch
        {
            "STORY_ARCHIVED" => "STORY_ARCHIVED",
            "STALE_MEDIA_RESULT" or "STALE_MEDIA_HANDOFF" or "STALE_JOB_RESULT" or
            "CONCURRENT_CLAIM_DETECTED" or "DUPLICATE_ACTIVE_JOB" => "STALE_MEDIA_RESULT",
            "IMAGE_ALIGNMENT_FAILED" or "IMAGE_SAFETY_FAILED" or
            "ILLUSTRATION_RETRY_EXHAUSTED" => "ILLUSTRATION_RETRY_EXHAUSTED",
            "TTS_SEGMENT_RETRY_EXHAUSTED" => "TTS_RETRY_EXHAUSTED",
            _ when unwrapped.Message.StartsWith("TTS_SEGMENT_RETRY_EXHAUSTED") => "TTS_RETRY_EXHAUSTED",
            _ when unwrapped.Message.StartsWith("AUDIO_QUALITY_GATE_DETERMINISTIC_FAILURE") => "TTS_RETRY_EXHAUSTED",
            "MEDIA_PACKAGE_INCOMPLETE" => "MEDIA_PACKAGE_INCOMPLETE",
            "INVALID_MEDIA_HANDOFF" => "INVALID_MEDIA_HANDOFF",
            _ when unwrapped is TimeoutException or OperationCanceledException => "MEDIA_PROVIDER_TIMEOUT",
            _ => "MEDIA_GENERATION_FAILED"
        };
    }

    private static bool IsPermanentFailure(Exception exception)
    {
        if (FindPermanentException(exception) is not null) return true;
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message is
                "INVALID_MEDIA_HANDOFF" or "INVALID_PERSISTED_SCENE_COVERAGE" or
                "INVALID_PERSISTED_ILLUSTRATION_BEATS" or "INVALID_ILLUSTRATION_BEAT_ASSET" or
                "SCENE_COVERAGE_EMPTY" or "INVALID_SCENE_ORDER" or "UNKNOWN_SCENE_BLOCK" or
                "NON_CONTIGUOUS_SCENE_BLOCKS" or "SCENE_TEXT_NOT_CANONICAL" or
                "INCOMPLETE_OR_OVERLAPPING_SCENE_COVERAGE" or
                "IMAGE_ALIGNMENT_FAILED" or "IMAGE_SAFETY_FAILED" or
                "MEDIA_EVALUATOR_NOT_CONFIGURED" or
                "STALE_MEDIA_HANDOFF" or "DUPLICATE_ACTIVE_JOB" or
                "STORY_NOT_FOUND")
            {
                return true;
            }
            if (current.Message.StartsWith("AUDIO_QUALITY_GATE_DETERMINISTIC_FAILURE", StringComparison.Ordinal) ||
                current.Message.StartsWith("TTS_SEGMENT_RETRY_EXHAUSTED", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static PermanentMediaGenerationException? FindPermanentException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PermanentMediaGenerationException permanent) return permanent;
        return null;
    }

    private static bool IsStale(Exception exception)
    {
        var unwrapped = UnwrapException(exception);
        return unwrapped.Message is
            "STALE_MEDIA_RESULT" or "STALE_MEDIA_HANDOFF" or "STALE_JOB_RESULT" or
            "STORY_ARCHIVED" or "CONCURRENT_CLAIM_DETECTED" or "DUPLICATE_ACTIVE_JOB";
    }

    private static Exception UnwrapException(Exception exception)
    {
        // knownCodes holds exact-string markers. Prefixed-message markers
        // (AUDIO_QUALITY_GATE_DETERMINISTIC_FAILURE:..., TTS_SEGMENT_RETRY_EXHAUSTED:...)
        // are also intentionally kept as permanent markers; callers pattern-match
        // on the prefix via StartsWith so we do not unwrap them either.
        var knownCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "STORY_ARCHIVED", "STALE_MEDIA_RESULT", "STALE_MEDIA_HANDOFF", "STALE_JOB_RESULT",
            "CONCURRENT_CLAIM_DETECTED", "DUPLICATE_ACTIVE_JOB",
            "ILLUSTRATION_RETRY_EXHAUSTED", "TTS_SEGMENT_RETRY_EXHAUSTED",
            "MEDIA_PACKAGE_INCOMPLETE", "INVALID_PERSISTED_SCENE_COVERAGE",
            "INVALID_MEDIA_HANDOFF", "STORY_NOT_FOUND"
        };
        var current = exception;
        while (current is TargetInvocationException { InnerException: not null } ||
               current is AggregateException { InnerException: not null })
        {
            current = current.InnerException!;
        }
        // The deterministic/retry-exhausted markers carry their root cause as a suffix
        // (e.g. "AUDIO_QUALITY_GATE_DETERMINISTIC_FAILURE:UNRECOGNIZED_AUDIO_HEADER"),
        // so we keep the original exception and do not peek inside.
        if (current is InvalidOperationException &&
            current.InnerException is not null &&
            !knownCodes.Contains(current.Message) &&
            !current.Message.StartsWith("AUDIO_QUALITY_GATE_DETERMINISTIC_FAILURE", StringComparison.Ordinal) &&
            !current.Message.StartsWith("TTS_SEGMENT_RETRY_EXHAUSTED", StringComparison.Ordinal))
        {
            current = current.InnerException;
        }
        return current;
    }

    private void RotateToken(StoryGenerationJob job)
    {
        // Attach using the observed token, then copy the new value even if another instance is tracked.
        _unitOfWork.Repository<StoryGenerationJob>().Update(job);
        job.ConcurrencyToken = Guid.NewGuid().ToString("N");
        _unitOfWork.Repository<StoryGenerationJob>().Update(job);
    }
    private sealed record MediaState(StoryGenerationJob Job, StoryVersion Version, Story Story);
}
