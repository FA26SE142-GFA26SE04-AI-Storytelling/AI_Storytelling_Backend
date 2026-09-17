using System.Reflection;
using System.Data;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed class MediaGenerationService : IMediaGenerationService, IMediaGenerationJobProcessor
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediaContextBuilder _contextBuilder;
    private readonly IStoryBlockParser _blockParser;
    private readonly ISceneSegmentationProvider _segmenter;
    private readonly ISceneCoverageValidator _coverageValidator;
    private readonly ISceneSpecificationBuilder _specificationBuilder;
    private readonly IImageGenerationProvider _imageProvider;
    private readonly ITtsProvider _ttsProvider;
    private readonly IMediaAlignmentEvaluator _alignmentEvaluator;
    private readonly IMediaSafetyEvaluator _safetyEvaluator;
    private readonly IMediaGenerationJobFailureFinalizer _failureFinalizer;
    private readonly TimeSpan _jobLease;
    private readonly int _assetMaxAttempts;

    public MediaGenerationService(
        IUnitOfWork unitOfWork,
        IMediaContextBuilder contextBuilder,
        IStoryBlockParser blockParser,
        ISceneSegmentationProvider segmenter,
        ISceneCoverageValidator coverageValidator,
        ISceneSpecificationBuilder specificationBuilder,
        IImageGenerationProvider imageProvider,
        ITtsProvider ttsProvider,
        IMediaAlignmentEvaluator alignmentEvaluator,
        IMediaSafetyEvaluator safetyEvaluator,
        IMediaGenerationJobFailureFinalizer failureFinalizer,
        MediaGenerationOptions options)
    {
        _unitOfWork = unitOfWork;
        _contextBuilder = contextBuilder;
        _blockParser = blockParser;
        _segmenter = segmenter;
        _coverageValidator = coverageValidator;
        _specificationBuilder = specificationBuilder;
        _imageProvider = imageProvider;
        _ttsProvider = ttsProvider;
        _alignmentEvaluator = alignmentEvaluator;
        _safetyEvaluator = safetyEvaluator;
        _failureFinalizer = failureFinalizer;
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
        var scenes = await _unitOfWork.Repository<StoryScene>().FindAsync(
            x => x.StoryVersionId == versionId, cancellationToken: cancellationToken);
        var assets = await _unitOfWork.Repository<MediaAsset>().FindAsync(
            x => x.StoryVersionId == versionId && x.StorySceneId != null, cancellationToken: cancellationToken);
        return new MediaGenerationProgress(
            storyId, versionId, story.Status.ToString(), job.Status.ToString(), scenes.Count,
            assets.Count(x => x.Type == MediaType.Illustration && x.Status == MediaStatus.Ready),
            assets.Count(x => x.Type == MediaType.TtsAudio && x.Status == MediaStatus.Ready),
            story.Status == StoryStatus.Ready, job.ErrorCode);
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
            // Re-fetch and validate status after acquiring lock to prevent race condition
            var revalidatedJob = await _unitOfWork.Repository<StoryGenerationJob>().GetByIdAsync(job.Id, cancellationToken);
            if (revalidatedJob is null || revalidatedJob.ConcurrencyToken != job.ConcurrencyToken)
                throw new InvalidOperationException("CONCURRENT_CLAIM_DETECTED");
            var story = await _unitOfWork.Repository<Story>().GetByIdAsync(job.StoryId, cancellationToken)
                        ?? throw new InvalidOperationException("STORY_NOT_FOUND");
            if (story.Status is not (StoryStatus.Approved or StoryStatus.MediaProcessing))
                throw new InvalidOperationException("STALE_MEDIA_HANDOFF");
            // Check for duplicate job: only one active/processing job per story
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

    private async Task ProcessClaimedAsync(int jobId, string claimedToken, CancellationToken cancellationToken)
    {
        var state = await LoadStateAsync(jobId, claimedToken, cancellationToken);
        var mediaContext = await GetOrCreateContextAsync(state, cancellationToken);
        var scenes = await GetOrCreateScenesAsync(state, claimedToken, mediaContext, cancellationToken);
        await UpdateStageAsync(state.Job.Id, claimedToken, JobStage.MediaGenerating, cancellationToken);
        foreach (var scene in scenes.OrderBy(x => x.SceneIndex))
        {
            await AssertFreshAsync(state.Job.Id, claimedToken, state.Version.Id, mediaContext.Id, cancellationToken);
            var specification = _specificationBuilder.Build(
                state.Version.Id, scene.Id, scene.SceneIndex, scene.SceneText,
                scene.VisualDescription, mediaContext.ContextJson);
            await EnsureIllustrationAsync(state.Job.Id, claimedToken, state.Version.Id, mediaContext.Id,
                scene, specification, cancellationToken);
            await EnsureAudioAsync(state.Job.Id, claimedToken, state.Version.Id, mediaContext.Id,
                scene, cancellationToken);
        }
        await UpdateStageAsync(state.Job.Id, claimedToken, JobStage.MediaFinalizing, cancellationToken);
        await FinalizeAsync(state.Job.Id, claimedToken, state.Version.Id, scenes.Count, cancellationToken);
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
        // Re-validate that no concurrent job created context while we were building
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

        // Check freshness BEFORE setting intermediate stage to avoid leaving job in incomplete state
        await AssertFreshAsync(state.Job.Id, claimedToken, state.Version.Id, mediaContext.Id, cancellationToken);
        await UpdateStageAsync(state.Job.Id, claimedToken, JobStage.MediaSegmenting, cancellationToken);
        var blocks = _blockParser.Parse(state.Version.Content!);
        var selections = await _segmenter.SegmentAsync(
            new SceneSegmentationRequest(state.Version.Content!, blocks, mediaContext.ContextJson), cancellationToken);
        var validated = _coverageValidator.ValidateAndAssemble(state.Version.Content!, blocks, selections);
        // Check freshness again after heavy operations before persisting
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

    private async Task EnsureIllustrationAsync(
        int jobId, string claimedToken, int versionId, int mediaContextId,
        StoryScene scene, SceneSpecification specification, CancellationToken cancellationToken)
    {
        var asset = await GetOrCreateAssetAsync(versionId, scene, MediaType.Illustration, cancellationToken);
        if (asset.Status == MediaStatus.Ready) return;
        Exception? lastError = null;
        for (var attempt = 0; attempt < _assetMaxAttempts; attempt++)
        {
            try
            {
                asset.Status = MediaStatus.Processing;
                _unitOfWork.Repository<MediaAsset>().Update(asset);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                var illustration = await _imageProvider.GenerateAsync(specification, cancellationToken);
                var alignment = await _alignmentEvaluator.EvaluateAsync(specification, illustration, cancellationToken);
                var safety = await _safetyEvaluator.EvaluateAsync(specification, illustration, cancellationToken);
                if (alignment.Reason == "MEDIA_EVALUATOR_NOT_CONFIGURED" || safety.Reason == "MEDIA_EVALUATOR_NOT_CONFIGURED")
                    throw new PermanentMediaGenerationException("MEDIA_EVALUATOR_NOT_CONFIGURED");
                if (!alignment.Passed || !safety.Passed)
                    throw new InvalidOperationException(!alignment.Passed ? "IMAGE_ALIGNMENT_FAILED" : "IMAGE_SAFETY_FAILED");
                await AssertFreshAsync(jobId, claimedToken, versionId, mediaContextId, cancellationToken);
                asset.Url = illustration.Url;
                asset.Status = MediaStatus.Ready;
                _unitOfWork.Repository<MediaAsset>().Update(asset);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && !IsStale(exception))
            {
                lastError = exception;
                asset.Status = MediaStatus.Failed;
                _unitOfWork.Repository<MediaAsset>().Update(asset);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        throw new InvalidOperationException("ILLUSTRATION_RETRY_EXHAUSTED", lastError);
    }

    private async Task EnsureAudioAsync(
        int jobId, string claimedToken, int versionId, int mediaContextId,
        StoryScene scene, CancellationToken cancellationToken)
    {
        var asset = await GetOrCreateAssetAsync(versionId, scene, MediaType.TtsAudio, cancellationToken);
        if (asset.Status == MediaStatus.Ready) return;
        Exception? lastError = null;
        for (var attempt = 0; attempt < _assetMaxAttempts; attempt++)
        {
            try
            {
                // Check freshness BEFORE generating to avoid discarding generated audio on stale
                await AssertFreshAsync(jobId, claimedToken, versionId, mediaContextId, cancellationToken);
                asset.Status = MediaStatus.Processing;
                _unitOfWork.Repository<MediaAsset>().Update(asset);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                var audio = await _ttsProvider.GenerateAsync(scene.SceneText, cancellationToken);
                // Verify freshness again after generation before persisting
                await AssertFreshAsync(jobId, claimedToken, versionId, mediaContextId, cancellationToken);
                asset.Url = audio.Url;
                asset.WordTimings = audio.WordTimingsJson;
                asset.Status = MediaStatus.Ready;
                _unitOfWork.Repository<MediaAsset>().Update(asset);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && !IsStale(exception))
            {
                lastError = exception;
                asset.Status = MediaStatus.Failed;
                _unitOfWork.Repository<MediaAsset>().Update(asset);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        throw new InvalidOperationException("TTS_RETRY_EXHAUSTED", lastError);
    }

    private async Task<MediaAsset> GetOrCreateAssetAsync(
        int versionId, StoryScene scene, MediaType type, CancellationToken cancellationToken)
    {
        var existing = await _unitOfWork.Repository<MediaAsset>().FirstOrDefaultAsync(
            x => x.StorySceneId == scene.Id && x.Type == type, cancellationToken: cancellationToken);
        if (existing is not null) return existing;
        var asset = new MediaAsset
        {
            StoryVersionId = versionId,
            StorySceneId = scene.Id,
            SceneIndex = scene.SceneIndex,
            Type = type,
            Status = MediaStatus.Queued
        };
        await _unitOfWork.Repository<MediaAsset>().AddAsync(asset, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return asset;
    }

    private async Task FinalizeAsync(
        int jobId, string token, int versionId, int sceneCount, CancellationToken cancellationToken)
    {
        var job = await RequireClaimedJobAsync(jobId, token, cancellationToken);
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(job.StoryId, cancellationToken)
                    ?? throw new InvalidOperationException("STORY_NOT_FOUND");
        if (story.Status == StoryStatus.Archived) throw new InvalidOperationException("STORY_ARCHIVED");
        var scenes = await _unitOfWork.Repository<StoryScene>().FindAsync(
            x => x.StoryVersionId == versionId, cancellationToken: cancellationToken);
        var assets = await _unitOfWork.Repository<MediaAsset>().FindAsync(
            x => x.StoryVersionId == versionId && x.StorySceneId != null, cancellationToken: cancellationToken);
        var ready = sceneCount > 0 && scenes.Count == sceneCount && scenes.All(scene =>
            assets.Count(asset => asset.StorySceneId == scene.Id && asset.Type == MediaType.Illustration && asset.Status == MediaStatus.Ready) == 1 &&
            assets.Count(asset => asset.StorySceneId == scene.Id && asset.Type == MediaType.TtsAudio && asset.Status == MediaStatus.Ready) == 1);
        if (!ready) throw new InvalidOperationException("MEDIA_PACKAGE_INCOMPLETE");

        story.Status = StoryStatus.Ready;
        _unitOfWork.Repository<Story>().Update(story);
        job.Status = GenerationJobStatus.Completed;
        job.Stage = JobStage.MediaCompleted;
        job.CompletedAt = DateTime.UtcNow;
        job.LeaseExpiresAt = null;
        RotateToken(job);
        _unitOfWork.Repository<StoryGenerationJob>().Update(job);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
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
        var contentPosition = 0; // Tracks character position in content, not scene index
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
        var permanent = FindPermanentException(exception);
        if (permanent is not null) return permanent.ErrorCode;
        // Unwrap InnerException to get structured error codes from wrapped exceptions
        var unwrapped = UnwrapException(exception);
        return unwrapped.Message switch
        {
            "STORY_ARCHIVED" => "STORY_ARCHIVED",
            "STALE_MEDIA_RESULT" or "STALE_MEDIA_HANDOFF" or "STALE_JOB_RESULT" or
            "CONCURRENT_CLAIM_DETECTED" or "DUPLICATE_ACTIVE_JOB" => "STALE_MEDIA_RESULT",
            "ILLUSTRATION_RETRY_EXHAUSTED" => "ILLUSTRATION_RETRY_EXHAUSTED",
            "TTS_RETRY_EXHAUSTED" => "TTS_RETRY_EXHAUSTED",
            "MEDIA_PACKAGE_INCOMPLETE" => "MEDIA_PACKAGE_INCOMPLETE",
            "INVALID_MEDIA_HANDOFF" => "INVALID_MEDIA_HANDOFF",
            _ => "MEDIA_GENERATION_FAILED"
        };
    }

    private static bool IsPermanentFailure(Exception exception)
    {
        if (FindPermanentException(exception) is not null) return true;
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current.Message is
                "INVALID_MEDIA_HANDOFF" or "INVALID_PERSISTED_SCENE_COVERAGE" or
                "SCENE_COVERAGE_EMPTY" or "INVALID_SCENE_ORDER" or "UNKNOWN_SCENE_BLOCK" or
                "NON_CONTIGUOUS_SCENE_BLOCKS" or "SCENE_TEXT_NOT_CANONICAL" or
                "INCOMPLETE_OR_OVERLAPPING_SCENE_COVERAGE" or "IMAGE_ALIGNMENT_FAILED" or
                "IMAGE_SAFETY_FAILED" or "STALE_MEDIA_HANDOFF" or "DUPLICATE_ACTIVE_JOB" or
                "STORY_NOT_FOUND") return true;
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
        // Unwrap TargetInvocationException, AggregateException, etc.
        // But NOT InvalidOperationException with our known error codes - those are intentional markers
        var knownCodes = new HashSet<string>
        {
            "STORY_ARCHIVED", "STALE_MEDIA_RESULT", "STALE_MEDIA_HANDOFF", "STALE_JOB_RESULT",
            "CONCURRENT_CLAIM_DETECTED", "DUPLICATE_ACTIVE_JOB",
            "ILLUSTRATION_RETRY_EXHAUSTED", "TTS_RETRY_EXHAUSTED",
            "MEDIA_PACKAGE_INCOMPLETE", "INVALID_PERSISTED_SCENE_COVERAGE",
            "INVALID_MEDIA_HANDOFF", "STORY_NOT_FOUND"
        };
        var current = exception;
        while (current is TargetInvocationException { InnerException: not null } ||
               current is AggregateException { InnerException: not null })
        {
            current = current.InnerException!;
        }
        // Only unwrap InvalidOperationException if it's NOT a known error code (those are intentional markers)
        if (current is InvalidOperationException && current.InnerException is not null && !knownCodes.Contains(current.Message))
        {
            current = current.InnerException;
        }
        return current;
    }

    private static void RotateToken(StoryGenerationJob job) => job.ConcurrencyToken = Guid.NewGuid().ToString("N");
    private sealed record MediaState(StoryGenerationJob Job, StoryVersion Version);
}
