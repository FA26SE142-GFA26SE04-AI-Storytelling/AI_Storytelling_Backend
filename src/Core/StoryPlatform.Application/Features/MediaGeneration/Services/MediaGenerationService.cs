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

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed class MediaGenerationService : IMediaGenerationService, IMediaGenerationJobProcessor
{
    private static readonly TimeSpan IllustrationUrlLifetime = TimeSpan.FromMinutes(5);
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
    private readonly TimeSpan _jobLease;
    private readonly int _assetMaxAttempts;

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
        ILogger<MediaGenerationService> logger)
    {
        _unitOfWork = unitOfWork;
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
        _jobLease = TimeSpan.FromMinutes(Math.Clamp(options.JobLeaseMinutes, 1, 30));
        _assetMaxAttempts = Math.Clamp(options.AssetMaxAttempts, 1, 5);
    }

    public async Task<MediaGenerationProgress> GetProgressAsync(
        int userId, int storyId, CancellationToken cancellationToken = default)
    {
        var story = await LoadAuthorizedStoryAsync(userId, storyId, cancellationToken);
        var job = (await _unitOfWork.Repository<StoryGenerationJob>().FindAsync(
                x => x.StoryId == storyId && x.Operation == GenerationJobOperation.GenerateMediaPackage,
                cancellationToken: cancellationToken))
            .OrderByDescending(x => x.Id).FirstOrDefault();
        if (job?.StoryVersionId is not int versionId)
            return new MediaGenerationProgress(storyId, null, story.Status.ToString(), "NotStarted", 0, 0, 0, false, null);
        var readiness = await _readinessService.CheckAsync(versionId, expectedSceneCount: null, cancellationToken);
        return new MediaGenerationProgress(
            storyId, versionId, story.Status.ToString(), job.Status.ToString(), readiness.SceneCount,
            readiness.ReadyIllustrations, readiness.ReadyAudio,
            story.Status == StoryStatus.Ready && readiness.IsReady, job.ErrorCode, readiness.RequiredIllustrations,
            readiness.MissingIllustrationBeatIds, readiness.MissingAudioSegmentIds,
            readiness.ManualReviewAssetIds, readiness.ScenesWithoutValidBeats);
    }

    public async Task<StoryMediaPackage> GetPackageAsync(int userId, int storyId,
        CancellationToken cancellationToken = default)
    {
        await LoadAuthorizedStoryAsync(userId, storyId, cancellationToken);
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
            x => x.StoryVersionId == version.Id && x.Type == MediaType.Illustration,
            cancellationToken: cancellationToken);
        var assetsByBeatId = assets.Where(asset => asset.IllustrationBeatId.HasValue)
            .GroupBy(asset => asset.IllustrationBeatId!.Value)
            .ToDictionary(group => group.Key, group => group.First());
        var sceneItems = new List<SceneMediaItem>(scenes.Length);
        foreach (var scene in scenes)
        {
            var illustrations = new List<IllustrationBeatMediaItem>();
            foreach (var beat in beats.Where(item => item.StorySceneId == scene.Id).OrderBy(item => item.BeatOrder))
            {
                assetsByBeatId.TryGetValue(beat.Id, out var asset);
                string? signedUrl = null;
                DateTimeOffset? expiresAt = null;
                if (asset?.Status == MediaStatus.Ready && !string.IsNullOrWhiteSpace(asset.Url))
                {
                    var requestedAt = DateTimeOffset.UtcNow;
                    try
                    {
                        signedUrl = await _mediaStorage.GetSignedUrlAsync(
                            asset.Url, IllustrationUrlLifetime, cancellationToken);
                    }
                    catch (Exception exception) when (
                        !cancellationToken.IsCancellationRequested &&
                        exception is (HttpRequestException or InvalidOperationException or ArgumentException or TaskCanceledException or JsonException))
                    {
                        _logger.LogWarning(
                            "Could not sign illustration URL for story {StoryId}, beat {BeatId}, asset {AssetId}. ErrorType={ErrorType}",
                            storyId, beat.Id, asset.Id, exception.GetType().Name);
                        throw new ServiceUnavailableException("Ảnh minh họa tạm thời chưa thể truy cập. Vui lòng thử lại sau.");
                    }
                    expiresAt = requestedAt.Add(IllustrationUrlLifetime);
                }

                illustrations.Add(new IllustrationBeatMediaItem(
                    beat.Id, beat.BeatOrder, beat.StartOffset, beat.EndOffset, beat.VisualFocus,
                    asset?.Id, asset?.Status.ToString() ?? "Queued", signedUrl, expiresAt));
            }
            sceneItems.Add(new SceneMediaItem(scene.Id, scene.SceneIndex, illustrations));
        }
        return new StoryMediaPackage(storyId, version.Id, sceneItems);
    }

    public async Task<MediaGenerationProgress> RetryAsync(
        int userId, int storyId, CancellationToken cancellationToken = default)
    {
        await LoadAuthorizedStoryAsync(userId, storyId, cancellationToken);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, cancellationToken);
            var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken)
                        ?? throw new NotFoundException("Story", storyId);
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

            var version = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(versionId, cancellationToken);
            if (version is null || version.StoryId != storyId || !version.IsCurrent || string.IsNullOrWhiteSpace(version.Content))
                throw new ConflictException("INVALID_MEDIA_HANDOFF");

            var assets = await _unitOfWork.Repository<MediaAsset>().FindAsync(
                x => x.StoryVersionId == versionId && x.Status != MediaStatus.Ready,
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

            job.Status = GenerationJobStatus.Pending;
            job.Stage = JobStage.MediaPending;
            job.AttemptNo = 0;
            job.ErrorCode = null;
            job.FallbackMessage = null;
            job.LeaseExpiresAt = null;
            job.CompletedAt = null;
            job.StartedAt = DateTime.UtcNow;
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
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        return await GetProgressAsync(userId, storyId, cancellationToken);
    }

    public async Task RegenerateIllustrationBeatAsync(int userId, int storyId, int beatId,
        CancellationToken cancellationToken = default)
    {
        await LoadAuthorizedStoryAsync(userId, storyId, cancellationToken);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, cancellationToken);
            var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken)
                ?? throw new NotFoundException("Story", storyId);
            if (story.Status is not (StoryStatus.MediaProcessing or StoryStatus.Ready))
                throw new ConflictException("MEDIA_REGENERATION_NOT_ALLOWED");
            var version = (await _unitOfWork.Repository<StoryVersion>().FindAsync(
                x => x.StoryId == storyId && x.IsCurrent, cancellationToken: cancellationToken)).FirstOrDefault()
                ?? throw new ConflictException("INVALID_MEDIA_HANDOFF");
            var beat = await _unitOfWork.Repository<IllustrationBeat>().GetByIdAsync(beatId, cancellationToken)
                ?? throw new NotFoundException("Illustration beat", beatId);
            var scene = await _unitOfWork.Repository<StoryScene>().GetByIdAsync(beat.StorySceneId, cancellationToken);
            if (scene is null || scene.StoryVersionId != version.Id)
                throw new ConflictException("BEAT_NOT_IN_CURRENT_VERSION");
            var asset = await _unitOfWork.Repository<MediaAsset>().FirstOrDefaultAsync(
                x => x.IllustrationBeatId == beatId && x.Type == MediaType.Illustration,
                cancellationToken: cancellationToken)
                ?? throw new NotFoundException("Illustration asset", beatId);
            if (asset.StorySceneId != scene.Id || asset.StoryVersionId != version.Id)
                throw new ConflictException("BEAT_ASSET_NOT_IN_CURRENT_VERSION");
            var job = (await _unitOfWork.Repository<StoryGenerationJob>().FindAsync(
                x => x.StoryId == storyId && x.StoryVersionId == version.Id &&
                     x.Operation == GenerationJobOperation.GenerateMediaPackage,
                cancellationToken: cancellationToken)).OrderByDescending(x => x.Id).FirstOrDefault()
                ?? throw new NotFoundException("Media generation job", storyId);
            if (job.Status == GenerationJobStatus.Processing)
                throw new ConflictException("MEDIA_JOB_PROCESSING");

            asset.Status = MediaStatus.Queued;
            asset.ValidationStatus = ValidationStatus.Pending;
            asset.AttemptCount = 0;
            asset.Url = null;
            asset.LastValidationReason = null;
            asset.ValidationResultJson = null;
            asset.CompletedAt = null;
            _unitOfWork.Repository<MediaAsset>().Update(asset);
            job.Status = GenerationJobStatus.Pending;
            job.Stage = JobStage.MediaPending;
            job.AttemptNo = 0;
            job.ErrorCode = null;
            job.FallbackMessage = null;
            job.LeaseExpiresAt = null;
            job.CompletedAt = null;
            _unitOfWork.Repository<StoryGenerationJob>().Update(job);
            RotateToken(job);
            story.Status = StoryStatus.MediaProcessing;
            _unitOfWork.Repository<Story>().Update(story);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            _logger.LogInformation("Illustration beat regeneration queued Story={StoryId} Version={VersionId} Beat={BeatId} Asset={AssetId}",
                storyId, version.Id, beat.Id, asset.Id);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    public async Task<MediaJobProcessResult> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var job = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(
            x => x.Operation == GenerationJobOperation.GenerateMediaPackage &&
                 x.AttemptNo < x.MaxAttempts &&
                 (x.Status == GenerationJobStatus.Pending ||
                  x.Status == GenerationJobStatus.Failed ||
                  (x.Status == GenerationJobStatus.Processing && x.LeaseExpiresAt < now)),
            cancellationToken: cancellationToken);
        if (job is null) return MediaJobProcessResult.NoJob;

        var preClaimToken = job.ConcurrencyToken;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(job.StoryId, cancellationToken);
            var revalidatedJob = await _unitOfWork.Repository<StoryGenerationJob>().GetByIdAsync(job.Id, cancellationToken);
            if (revalidatedJob is null || revalidatedJob.ConcurrencyToken != job.ConcurrencyToken)
                throw new InvalidOperationException("CONCURRENT_CLAIM_DETECTED");
            var story = await _unitOfWork.Repository<Story>().GetByIdAsync(job.StoryId, cancellationToken)
                        ?? throw new InvalidOperationException("STORY_NOT_FOUND");
            if (story.Status is not (StoryStatus.Approved or StoryStatus.MediaProcessing))
                throw new InvalidOperationException("STALE_MEDIA_HANDOFF");
            var existingActiveJob = (await _unitOfWork.Repository<StoryGenerationJob>().FindAsync(
                x => x.StoryId == story.Id &&
                     x.Operation == GenerationJobOperation.GenerateMediaPackage &&
                     (x.Status == GenerationJobStatus.Processing || x.Status == GenerationJobStatus.Pending) &&
                     x.Id != job.Id,
                cancellationToken: cancellationToken)).FirstOrDefault();
            if (existingActiveJob is not null)
                throw new InvalidOperationException("DUPLICATE_ACTIVE_JOB");
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
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
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
            await _failureFinalizer.MarkFailedAsync(
                job.Id, claimedToken, errorCode, CancellationToken.None);
            return MediaJobProcessResult.Failed(isPermanent, errorCode);
        }
        return MediaJobProcessResult.Completed;
    }

    /// <summary>
    /// Resets a segment TTS audio to Queued and triggers regeneration.
    /// Used when a supervisor manually rejects a segment's audio.
    /// </summary>
    public async Task RegenerateSegmentAudioAsync(
        int userId, int storyId, int segmentId, CancellationToken cancellationToken = default)
    {
        await LoadAuthorizedStoryAsync(userId, storyId, cancellationToken);
        var segment = await _unitOfWork.Repository<StorySegment>().GetByIdAsync(segmentId, cancellationToken)
                      ?? throw new NotFoundException("StorySegment", segmentId);
        var asset = await _unitOfWork.Repository<MediaAsset>().FirstOrDefaultAsync(
            x => x.StorySegmentId == segmentId && x.Type == MediaType.TtsAudio,
            cancellationToken: cancellationToken);
        if (asset is null) throw new NotFoundException("TTS audio asset", segmentId);
        asset.Status = MediaStatus.Queued;
        asset.ValidationStatus = ValidationStatus.Pending;
        asset.AttemptCount = 0;
        asset.LastValidationReason = null;
        asset.Url = null;
        asset.CompletedAt = null;
        _unitOfWork.Repository<MediaAsset>().Update(asset);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessClaimedAsync(int jobId, string claimedToken, CancellationToken cancellationToken)
    {
        var state = await LoadStateAsync(jobId, claimedToken, cancellationToken);
        var mediaContext = await GetOrCreateContextAsync(state, cancellationToken);
        var scenes = await GetOrCreateScenesAsync(state, claimedToken, mediaContext, cancellationToken);
        await UpdateStageAsync(state.Job.Id, claimedToken, JobStage.MediaGenerating, cancellationToken);

        foreach (var scene in scenes.OrderBy(x => x.SceneIndex))
        {
            await AssertFreshAsync(state.Job.Id, claimedToken, state.Version.Id, mediaContext.Id, cancellationToken);
            var baseSpecification = _specificationBuilder.Build(
                state.Version.Id, scene.Id, scene.SceneIndex, scene.SceneText,
                scene.VisualDescription, mediaContext.ContextJson);

            var beats = await GetOrCreateBeatsAsync(state.Job.Id, claimedToken, state.Version.Id,
                mediaContext, scene, cancellationToken);
            foreach (var beat in beats.OrderBy(x => x.BeatOrder))
            {
                var beatText = scene.SceneText[beat.StartOffset..beat.EndOffset];
                await EnsureIllustrationAsync(
                    state.Job.Id, claimedToken, state.Job.StoryId, state.Version.Id, mediaContext,
                    scene, beat, baseSpecification.ForBeat(beatText, beat.VisualFocus), cancellationToken);
            }

            // Audio: segment-level, one asset per StorySegment.
            await EnsureAudioSegmentsAsync(
                state.Job.Id, claimedToken, state.Job.StoryId, state.Version.Id, mediaContext,
                scene, cancellationToken);
        }

        await UpdateStageAsync(state.Job.Id, claimedToken, JobStage.MediaFinalizing, cancellationToken);
        await FinalizeAsync(state.Job.Id, claimedToken, state.Version.Id, mediaContext.Id,
            scenes.Count, cancellationToken);
    }

    private async Task<MediaContext> GetOrCreateContextAsync(MediaState state, CancellationToken cancellationToken)
    {
        var existing = (await _unitOfWork.Repository<MediaContext>().FindAsync(
                x => x.StoryVersionId == state.Version.Id, cancellationToken: cancellationToken))
            .OrderByDescending(x => x.Revision).FirstOrDefault();
        if (existing is not null) return existing;

        StoryGenerationRequest? request = null;
        if (state.Job.GenerationRequestId.HasValue)
            request = await _unitOfWork.Repository<StoryGenerationRequest>()
                .GetByIdAsync(state.Job.GenerationRequestId.Value, cancellationToken);
        var context = new MediaContext
        {
            StoryVersionId = state.Version.Id,
            Revision = 1,
            ContextJson = _contextBuilder.Build(new MediaContextBuildRequest(
                state.Version.Id, state.Version.Title, state.Version.Content!, state.Version.Lesson,
                state.Version.OutlineOpening, state.Version.OutlineDevelopment, state.Version.OutlineEnding,
                request?.AcceptedInputJson, request?.ContextSnapshotJson))
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
            x => x.StorySceneId == scene.Id && x.Type == MediaType.Illustration,
            cancellationToken: cancellationToken);
        IReadOnlyList<IllustrationBeatSelection> selections = [];
        if (legacyAsset is null)
        {
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
            StorySceneId = scene.Id, BeatOrder = x.BeatOrder, StartOffset = x.StartOffset,
            EndOffset = x.EndOffset, VisualFocus = x.VisualFocus.Trim()
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

        for (var attempt = 0; attempt < _assetMaxAttempts; attempt++)
        {
            try
            {
                await AssertFreshAsync(jobId, claimedToken, versionId, mediaContext.Id, cancellationToken);
                asset.Status = MediaStatus.Processing;
                asset.AttemptCount = attempt + 1;
                _unitOfWork.Repository<MediaAsset>().Update(asset);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // Build specification with corrective feedback from previous attempts.
                var specification = BuildSpecificationWithFeedback(baseSpecification, failureHistory);
                var illustration = await _imageProvider.GenerateAsync(specification, cancellationToken);
                var alignment = await _alignmentEvaluator.EvaluateAsync(specification, illustration, cancellationToken);
                var safety = await _safetyEvaluator.EvaluateAsync(specification, illustration, cancellationToken);

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
                var storagePath = $"{storyId}/v{mediaContext.Revision}/scene-{scene.SceneIndex}-beat-{beat.BeatOrder}-asset-{asset.Id}-{Guid.NewGuid():N}-a{attempt + 1}{illustration.SuggestedExtension}";
                await using var content = illustration.OpenReadStream();
                asset.Url = await _mediaStorage.UploadAsync(
                    storagePath, content, illustration.MimeType, cancellationToken);
                asset.MimeType = illustration.MimeType;
                asset.Provider = illustration.GetMetadata("provider") ?? "Gemini";
                asset.Model = illustration.GetMetadata("model") ?? "unknown";
                asset.Status = MediaStatus.Ready;
                asset.ValidationStatus = ValidationStatus.Passed;
                asset.CompletedAt = DateTime.UtcNow;
                _unitOfWork.Repository<MediaAsset>().Update(asset);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Illustration ready Story={StoryId} Version={VersionId} Scene={SceneId} Beat={BeatId} Asset={AssetId} Attempt={Attempt}",
                    storyId, versionId, scene.Id, beat.Id, asset.Id, asset.AttemptCount);
                return;
            }
            catch (PermanentMediaGenerationException) when (!cancellationToken.IsCancellationRequested)
            {
                // Permanent failure — record but break out of retry loop and rethrow.
                asset.Status = MediaStatus.Failed;
                _unitOfWork.Repository<MediaAsset>().Update(asset);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                throw;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && !IsStale(exception))
            {
                asset.Status = MediaStatus.Failed;
                _unitOfWork.Repository<MediaAsset>().Update(asset);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                if (exception is TransientMediaGenerationException) throw;
            }
        }

        // Retry exhausted — propagate original failure code so the public error reflects what went wrong.
        asset.Status = MediaStatus.ManualReview;
        _unitOfWork.Repository<MediaAsset>().Update(asset);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        var finalCode = failureHistory.Count > 0
            ? failureHistory[^1].Code
            : "ILLUSTRATION_RETRY_EXHAUSTED";
        throw new InvalidOperationException(finalCode);
    }

    private async Task EnsureAudioSegmentsAsync(
        int jobId, string claimedToken, int storyId, int versionId, MediaContext mediaContext,
        StoryScene scene, CancellationToken cancellationToken)
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

        // Re-fetch persisted segments with their IDs.
        var persistedSegments = (await _unitOfWork.Repository<StorySegment>().FindAsync(
            x => x.StorySceneId == scene.Id, cancellationToken: cancellationToken))
            .OrderBy(x => x.SegmentOrder).ToList();

        foreach (var segment in persistedSegments)
        {
            await AssertFreshAsync(jobId, claimedToken, versionId, mediaContext.Id, cancellationToken);
            await EnsureSegmentAudioAsync(jobId, claimedToken, storyId, versionId, mediaContext,
                scene, segment, cancellationToken);
        }
    }

    private async Task EnsureSegmentAudioAsync(
        int jobId, string claimedToken, int storyId, int versionId, MediaContext mediaContext,
        StoryScene scene, StorySegment segment, CancellationToken cancellationToken)
    {
        var asset = await GetOrCreateSegmentAudioAssetAsync(versionId, segment, cancellationToken);
        if (asset.Status == MediaStatus.Ready) return;

        var failureHistory = new List<(string Code, string Reason)>();

        for (var attempt = 0; attempt < _assetMaxAttempts; attempt++)
        {
            try
            {
                await AssertFreshAsync(jobId, claimedToken, versionId, mediaContext.Id, cancellationToken);
                asset.Status = MediaStatus.Processing;
                asset.AttemptCount = attempt + 1;
                _unitOfWork.Repository<MediaAsset>().Update(asset);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var audio = await _ttsProvider.GenerateAsync(segment.TextContent, cancellationToken);

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
                        _unitOfWork.Repository<MediaAsset>().Update(asset);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                        throw new InvalidOperationException($"AUDIO_QUALITY_GATE_DETERMINISTIC_FAILURE:{reason}");
                    }

                    asset.Status = MediaStatus.Failed;
                    _unitOfWork.Repository<MediaAsset>().Update(asset);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    continue; // retry for transient timing / count issues
                }

                await AssertFreshAsync(jobId, claimedToken, versionId, mediaContext.Id, cancellationToken);
                var storagePath = $"{storyId}/v{mediaContext.Revision}/audio-s{scene.SceneIndex}-{segment.SegmentOrder}-a{attempt + 1}{audio.SuggestedExtension}";
                await using var content = audio.OpenReadStream();
                asset.Url = await _mediaStorage.UploadAsync(
                    storagePath, content, audio.MimeType, cancellationToken);
                asset.MimeType = audio.MimeType;
                asset.WordTimings = audio.GetMetadata("wordTimingsJson");
                asset.Provider = audio.GetMetadata("provider") ?? "GoogleCloud";
                asset.Model = audio.GetMetadata("model") ?? "unknown";
                asset.Status = MediaStatus.Ready;
                asset.ValidationStatus = ValidationStatus.Passed;
                asset.CompletedAt = DateTime.UtcNow;
                _unitOfWork.Repository<MediaAsset>().Update(asset);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Audio ready Story={StoryId} Version={VersionId} Scene={SceneId} Segment={SegmentId} Asset={AssetId} Attempt={Attempt}",
                    storyId, versionId, scene.Id, segment.Id, asset.Id, asset.AttemptCount);
                return;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && !IsStale(exception))
            {
                if (exception is TransientMediaGenerationException) throw;
                if (exception is InvalidOperationException ioe && ioe.Message.StartsWith("AUDIO_QUALITY_GATE_DETERMINISTIC_FAILURE"))
                {
                    throw;
                }

                asset.Status = MediaStatus.Failed;
                _unitOfWork.Repository<MediaAsset>().Update(asset);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        asset.Status = MediaStatus.ManualReview;
        _unitOfWork.Repository<MediaAsset>().Update(asset);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        var finalReason = failureHistory.Count > 0
            ? failureHistory[^1].Reason
            : "TTS_SEGMENT_RETRY_EXHAUSTED";
        throw new InvalidOperationException($"TTS_SEGMENT_RETRY_EXHAUSTED:{finalReason}");
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
            x => x.IllustrationBeatId == beat.Id && x.Type == MediaType.Illustration,
            cancellationToken: cancellationToken);
        if (existing is not null)
        {
            if (existing.StorySceneId != scene.Id || existing.StoryVersionId != versionId)
                throw new InvalidOperationException("INVALID_ILLUSTRATION_BEAT_ASSET");
            return existing;
        }
        var asset = new MediaAsset
        {
            StoryVersionId = versionId,
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
            x => x.StorySegmentId == segment.Id && x.Type == MediaType.TtsAudio,
            cancellationToken: cancellationToken);
        if (existing is not null) return existing;
        var asset = new MediaAsset
        {
            StoryVersionId = versionId,
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
        var job = await RequireClaimedJobAsync(jobId, token, cancellationToken);
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(job.StoryId, cancellationToken)
                    ?? throw new InvalidOperationException("STORY_NOT_FOUND");
        if (story.Status == StoryStatus.Archived) throw new InvalidOperationException("STORY_ARCHIVED");
        var readiness = await _readinessService.CheckAsync(versionId, sceneCount, cancellationToken);
        if (!readiness.IsReady)
        {
            _logger.LogWarning(
                "Media package incomplete Story={StoryId} Version={VersionId} MissingScenes={MissingSceneCount} MissingBeats={MissingBeatCount} MissingAudio={MissingAudioCount} ManualReview={ManualReviewCount}",
                story.Id, versionId, readiness.ScenesWithoutValidBeats.Count,
                readiness.MissingIllustrationBeatIds.Count, readiness.MissingAudioSegmentIds.Count,
                readiness.ManualReviewAssetIds.Count);
            throw new InvalidOperationException("MEDIA_PACKAGE_INCOMPLETE");
        }

        // Re-check the current version, latest context, job lease and story state at the handoff.
        await AssertFreshAsync(jobId, token, versionId, mediaContextId, cancellationToken);

        story.Status = StoryStatus.Ready;
        _unitOfWork.Repository<Story>().Update(story);
        job.Status = GenerationJobStatus.Completed;
        job.Stage = JobStage.MediaCompleted;
        job.CompletedAt = DateTime.UtcNow;
        job.LeaseExpiresAt = null;
        RotateToken(job);
        _unitOfWork.Repository<StoryGenerationJob>().Update(job);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Media package ready Story={StoryId} Version={VersionId} Beats={BeatCount} Audio={AudioCount}",
            story.Id, versionId, readiness.RequiredIllustrations, readiness.ReadyAudio);
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

    private static string BuildValidationResultJson(MediaEvaluationResult alignment, MediaEvaluationResult safety)
    {
        return JsonSerializer.Serialize(new
        {
            alignmentScore = alignment.Decision.ToString(),
            alignmentReason = alignment.Reason,
            safetyScore = safety.Decision.ToString(),
            safetyReason = safety.Reason,
            evaluatedAt = DateTime.UtcNow
        });
    }

    private async Task AssertFreshAsync(
        int jobId, string token, int versionId, int contextId, CancellationToken cancellationToken)
    {
        var job = await RequireClaimedJobAsync(jobId, token, cancellationToken);
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(job.StoryId, cancellationToken);
        var version = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(versionId, cancellationToken);
        var latestContext = (await _unitOfWork.Repository<MediaContext>().FindAsync(
                x => x.StoryVersionId == versionId, cancellationToken: cancellationToken))
            .OrderByDescending(x => x.Revision).FirstOrDefault();
        if (story?.Status != StoryStatus.MediaProcessing || version is null || !version.IsCurrent ||
            job.StoryVersionId != versionId || latestContext is null || latestContext.Id != contextId)
            throw new InvalidOperationException("STALE_MEDIA_RESULT");
    }

    private async Task AssertHandoffFreshAsync(
        int jobId, string token, int versionId, CancellationToken cancellationToken)
    {
        var job = await RequireClaimedJobAsync(jobId, token, cancellationToken);
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(job.StoryId, cancellationToken);
        var version = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(versionId, cancellationToken);
        if (story?.Status != StoryStatus.MediaProcessing || version is null || !version.IsCurrent ||
            job.StoryVersionId != versionId)
            throw new InvalidOperationException("STALE_MEDIA_RESULT");
    }

    private async Task<MediaState> LoadStateAsync(int jobId, string token, CancellationToken cancellationToken)
    {
        var job = await RequireClaimedJobAsync(jobId, token, cancellationToken);
        if (!job.StoryVersionId.HasValue) throw new InvalidOperationException("INVALID_MEDIA_HANDOFF");
        var version = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(job.StoryVersionId.Value, cancellationToken);
        if (version is null || version.StoryId != job.StoryId || !version.IsCurrent || string.IsNullOrEmpty(version.Content))
            throw new InvalidOperationException("INVALID_MEDIA_HANDOFF");
        return new MediaState(job, version);
    }

    private async Task<StoryGenerationJob> RequireClaimedJobAsync(
        int jobId, string token, CancellationToken cancellationToken)
    {
        var job = await _unitOfWork.Repository<StoryGenerationJob>().GetByIdAsync(jobId, cancellationToken);
        if (job is null || job.Status != GenerationJobStatus.Processing || job.ConcurrencyToken != token)
            throw new InvalidOperationException("STALE_JOB_RESULT");
        return job;
    }

    private async Task UpdateStageAsync(
        int jobId, string claimedToken, JobStage stage, CancellationToken cancellationToken)
    {
        var job = await RequireClaimedJobAsync(jobId, claimedToken, cancellationToken);
        job.Stage = stage;
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
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story", storyId);
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

    private static void RotateToken(StoryGenerationJob job) => job.ConcurrencyToken = Guid.NewGuid().ToString("N");
    private sealed record MediaState(StoryGenerationJob Job, StoryVersion Version);
}
