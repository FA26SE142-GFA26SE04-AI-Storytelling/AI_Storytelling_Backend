using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Common;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.ContentGeneration;
using StoryPlatform.Application.Features.ContentGeneration.DTOs;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration.Quality;
using StoryPlatform.Application.Features.ExistingStories.Interfaces;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.StoryReview.Interfaces;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentGeneration.Services;

public sealed partial class ContentGenerationService : IContentGenerationService, IContentGenerationJobProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAIStoryGenerationClient _aiClient;
    private readonly IContentQualityEvaluator _qualityEvaluator;
    private readonly IContentGenerationJobFailureFinalizer _failureFinalizer;
    private readonly IStableVersionArtifactHandoffService _artifactHandoff;
    private readonly IStoryReviewService? _reviewService;
    private readonly IGrayZoneEvaluator? _moderation;
    private readonly GrayZoneWorkflowService? _grayZone;
    private readonly int _maxRefinementAttempts;
    private readonly int _artifactMaxAttempts;
    private readonly TimeSpan _jobLease;
    private readonly ListeningInteractionGenerator _listeningGenerator;
    private readonly int _contentExecutionBudgetSeconds;
    private readonly ILearningBatchService? _learningBatches;
    private readonly ILogger<ContentGenerationService> _logger;

    public ContentGenerationService(
        IUnitOfWork unitOfWork,
        IAIStoryGenerationClient aiClient,
        IContentQualityEvaluator qualityEvaluator,
        IContentGenerationJobFailureFinalizer failureFinalizer,
        IStableVersionArtifactHandoffService artifactHandoff,
        ContentGenerationOptions options,
        IStoryReviewService? reviewService = null,
        IGrayZoneEvaluator? moderation = null, GrayZoneWorkflowService? grayZone = null, ILearningBatchService? learningBatches = null,
        ILogger<ContentGenerationService>? logger = null,
        ListeningInteractionGenerator? listeningGenerator = null)
    {
        _unitOfWork = unitOfWork;
        _aiClient = aiClient;
        _qualityEvaluator = qualityEvaluator;
        _failureFinalizer = failureFinalizer;
        _artifactHandoff = artifactHandoff;
        _reviewService = reviewService;
        _learningBatches = learningBatches;
        _logger = logger ?? NullLogger<ContentGenerationService>.Instance;
        _moderation = moderation; _grayZone = grayZone;
        _maxRefinementAttempts = Math.Clamp(options.MaxContentRefinementAttempts, 0, 2);
        _artifactMaxAttempts = Math.Clamp(options.ArtifactMaxAttempts, 1, 3);
        _jobLease = TimeSpan.FromMinutes(Math.Clamp(options.JobLeaseMinutes, 1, 30));
        _listeningGenerator = listeningGenerator ?? new ListeningInteractionGenerator(unitOfWork, aiClient, options);
        _contentExecutionBudgetSeconds = Math.Clamp(options.ContentExecutionBudgetSeconds, 6, 180);
    }

    public async Task<ContentGenerationProgressDto> GetProgressAsync(
        int userId, int storyId, CancellationToken cancellationToken = default)
    {
        var story = await LoadAuthorizedStoryAsync(userId, storyId, cancellationToken);
        var jobs = await _unitOfWork.Repository<StoryGenerationJob>().FindAsync(
            item => item.StoryId == storyId && !item.IsDeleted &&
                    (item.Operation == GenerationJobOperation.GenerateContent ||
                     item.Operation == GenerationJobOperation.GenerateVocabulary ||
                     item.Operation == GenerationJobOperation.GenerateQuiz ||
                     item.Operation == GenerationJobOperation.GenerateDiscussion ||
                     item.Operation == GenerationJobOperation.GenerateListeningInteraction),
            cancellationToken: cancellationToken);
        var stable = (await _unitOfWork.Repository<StoryVersion>().FindAsync(
                item => item.StoryId == storyId && item.IsCurrent && item.Content != null,
                cancellationToken: cancellationToken))
            .OrderByDescending(item => item.VersionNo)
            .FirstOrDefault();
        var vocabularyDone = stable is not null && await _unitOfWork.Repository<StoryVocabulary>()
            .ExistsAsync(item => item.StoryVersionId == stable.Id, cancellationToken);
        var quizDone = stable is not null && await _unitOfWork.Repository<QuizItem>()
            .ExistsAsync(item => item.StoryVersionId == stable.Id, cancellationToken);
        // Mỗi loại kế tiếp được tạo cùng transaction với loại vừa lưu.
        var listeningJobs = jobs.Where(item => item.Operation == GenerationJobOperation.GenerateListeningInteraction &&
            stable is not null && item.StoryVersionId == stable.Id).ToArray();
        var listeningDone = listeningJobs.All(item => item.Status == GenerationJobStatus.Completed);
        var active = jobs.Where(item => item.Status is GenerationJobStatus.Pending or GenerationJobStatus.Processing)
            .OrderByDescending(item => item.Id).FirstOrDefault();
        var batches = _learningBatches is not null && stable is not null
            ? await _unitOfWork.Repository<LearningArtifactGenerationBatch>().FindAsync(x => x.StoryVersionId == stable.Id, cancellationToken: cancellationToken)
            : Array.Empty<LearningArtifactGenerationBatch>();
        var failed = jobs.Where(item => item.Operation == GenerationJobOperation.GenerateContent ||
                stable is not null && item.StoryVersionId == stable.Id)
            .GroupBy(item => (item.Operation, item.ListeningInteractionType)).Select(group => group.OrderByDescending(item => item.Id).First())
            .Where(item => item.Status == GenerationJobStatus.Failed && (item.Operation switch
            {
                GenerationJobOperation.GenerateContent => stable is null || item.ErrorCode == "CONTENT_ARTIFACT_HANDOFF_FAILED",
                GenerationJobOperation.GenerateVocabulary => (!vocabularyDone || batches.Any(x => x.RootJobId == item.Id)) && item.StoryVersionId == stable?.Id,
                GenerationJobOperation.GenerateQuiz => !quizDone && item.StoryVersionId == stable?.Id,
                GenerationJobOperation.GenerateDiscussion => item.StoryVersionId == stable?.Id,
                GenerationJobOperation.GenerateListeningInteraction => item.StoryVersionId == stable?.Id,
                _ => false
            }))
            .OrderByDescending(item => item.CompletedAt ?? item.StartedAt).ThenByDescending(item => item.Id).FirstOrDefault();
        var waiting = jobs.Any(x => x.Status == GenerationJobStatus.WaitingForModeration);
        var qualityFailure = active is null && !waiting ? ToQualityFailure(failed) : null;
        var readingMediaOnly = story.OutputMode == StoryOutputMode.ReadingMediaOnly;
        var contentComplete = stable is not null && story.Status is
            (StoryStatus.ContentReview or StoryStatus.Approved or StoryStatus.MediaProcessing or StoryStatus.MediaReview or StoryStatus.Ready);
        var isComplete = active is null && !waiting && contentComplete && (readingMediaOnly || vocabularyDone && quizDone && listeningDone);
        var contentJob = jobs.Where(x => x.Operation == GenerationJobOperation.GenerateContent).OrderByDescending(x => x.Id).FirstOrDefault();
        CandidateMetadataEnvelope? latestCandidate = null;
        if (!string.IsNullOrWhiteSpace(contentJob?.GenerationMetadataJson))
        {
            try { latestCandidate = JsonSerializer.Deserialize<CandidateMetadataEnvelope>(contentJob.GenerationMetadataJson, JsonOptions); }
            catch (JsonException) { /* Legacy metadata must not break progress. */ }
        }
        var request = contentJob?.GenerationRequestId is int requestId
            ? await _unitOfWork.Repository<StoryGenerationRequest>().GetByIdAsync(requestId, cancellationToken) : null;
        AIStoryInputContextSnapshot? pinnedContext = null;
        if (!string.IsNullOrWhiteSpace(request?.ContextSnapshotJson))
        {
            try { pinnedContext = JsonSerializer.Deserialize<AIStoryInputContextSnapshot>(request.ContextSnapshotJson, JsonOptions); }
            catch (JsonException) { }
        }

        return new ContentGenerationProgressDto
        {
            ElapsedSeconds = contentJob is null || contentJob.Status == GenerationJobStatus.Pending ? null
                : Math.Max(0, ((contentJob.CompletedAt ?? DateTime.UtcNow) - contentJob.StartedAt).TotalSeconds),
            RefinementAttempts = latestCandidate?.RefinementAttempts,
            LastMeasuredWordCount = latestCandidate?.LastMeasuredWordCount,
            MinimumWords = pinnedContext?.LearningProfile?.MinimumWords,
            MaximumWords = pinnedContext is null ? null : Constraints(pinnedContext).MaximumWords,
            VocabularyTarget = batches.Where(x => x.Kind == "vocabulary").Sum(x => x.TargetCount),
            QuizTarget = batches.Where(x => x.Kind.StartsWith("quiz_")).Sum(x => x.TargetCount),
            VocabularyCount = stable is null ? 0 : await _unitOfWork.Repository<StoryVocabulary>().CountAsync(x => x.StoryVersionId == stable.Id, cancellationToken),
            QuizCount = stable is null ? 0 : await _unitOfWork.Repository<QuizItem>().CountAsync(x => x.StoryVersionId == stable.Id, cancellationToken),
            TotalLearningBatches = batches.Count,
            CompletedLearningBatches = batches.Count(x => x.Status == GenerationJobStatus.Completed),
            LearningWarnings = batches.Where(x => !string.IsNullOrWhiteSpace(x.ShortfallReason)).Select(x => x.ShortfallReason!).Distinct().ToArray(),
            StoryId = story.Id,
            OutputMode = StoryPlatform.Application.Common.StoryOutputModeContract.Format(story.OutputMode),
            StoryStatus = ToSnake(story.Status.ToString()),
            WaitingForModeration = waiting,
            CurrentStep = waiting ? "waiting_for_moderation" : active is not null
                    ? $"{(active.Status == GenerationJobStatus.Processing ? "generating" : "pending")}_{OperationName(active.Operation)}"
                    : isComplete ? "complete" : failed is not null ? $"failed_{OperationName(failed.Operation)}" : "not_started",
            Content = stable is not null ? "stable" : JobState(jobs, GenerationJobOperation.GenerateContent),
            Vocabulary = readingMediaOnly ? "skipped" : vocabularyDone ? "completed" : JobState(jobs, GenerationJobOperation.GenerateVocabulary),
            Quiz = readingMediaOnly ? "skipped" : quizDone ? "completed" : JobState(jobs, GenerationJobOperation.GenerateQuiz),
            Listening = readingMediaOnly ? "skipped" :
                listeningJobs.Length == 0 ? (quizDone && contentComplete ? "skipped" : "not_started") :
                listeningDone ? "completed" : JobState(listeningJobs, GenerationJobOperation.GenerateListeningInteraction),
            IsComplete = isComplete,
            LastErrorCode = active is null && !waiting && !isComplete ? failed?.ErrorCode : null,
            QualityFailure = isComplete ? null : qualityFailure,
            StableStoryVersionId = stable?.Id
        };
    }

    public async Task<ContentGenerationProgressDto> RetryAsync(
        int userId,
        int storyId,
        RetryContentGenerationRequestDto input,
        CancellationToken cancellationToken = default)
    {
        ValidateObject(input);
        if (await RetryStableHandoffAsync(userId, storyId, input.RetryKey.Trim(), cancellationToken))
            return await GetProgressAsync(userId, storyId, cancellationToken);
        if (_learningBatches is not null && await RetryLearningArtifactsAsync(userId, storyId, input.RetryKey.Trim(), cancellationToken))
            return await GetProgressAsync(userId, storyId, cancellationToken);
        await InStoryTransactionAsync(storyId, async () =>
        {
            var story = await LoadAuthorizedStoryAsync(userId, storyId, cancellationToken);
            if (story.Source != StorySource.Ai || story.Status != StoryStatus.OutlineReview)
            {
                throw new ConflictException("Chỉ Story AI đang ở trạng thái outline_review mới có thể retry sinh nội dung.");
            }

            var outline = (await _unitOfWork.Repository<StoryVersion>().FindAsync(
                    item => item.StoryId == storyId &&
                            item.IsCurrent &&
                            item.Content == null &&
                            item.OutlineApprovedAt != null,
                    cancellationToken: cancellationToken))
                .OrderByDescending(item => item.VersionNo)
                .FirstOrDefault();
            if (outline is null)
            {
                throw new ConflictException("Story không có outline hiện hành đã được duyệt để retry sinh nội dung.");
            }

            var key = input.RetryKey.Trim();
            var existing = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(
                item => item.RequestedByUserId == userId &&
                        item.Operation == GenerationJobOperation.GenerateContent &&
                        item.OperationKey == key,
                cancellationToken: cancellationToken);
            if (existing is not null)
            {
                if (existing.StoryId != storyId || existing.BaseStoryVersionId != outline.Id)
                {
                    throw new ConflictException("Retry key đã được sử dụng cho Story hoặc outline version khác.");
                }

                return true;
            }

            if (await _unitOfWork.Repository<StoryGenerationJob>().ExistsAsync(
                    item => item.StoryId == storyId &&
                            (item.Operation == GenerationJobOperation.GenerateContent ||
                             item.Operation == GenerationJobOperation.GenerateVocabulary ||
                             item.Operation == GenerationJobOperation.GenerateQuiz ||
                             item.Operation == GenerationJobOperation.GenerateDiscussion ||
                             item.Operation == GenerationJobOperation.GenerateListeningInteraction) &&
                            (item.Status == GenerationJobStatus.Pending || item.Status == GenerationJobStatus.Processing),
                    cancellationToken))
            {
                throw new ConflictException("Story đang có content generation job hoạt động.");
            }

            var failed = (await _unitOfWork.Repository<StoryGenerationJob>().FindAsync(
                    item => item.StoryId == storyId &&
                            (item.Operation == GenerationJobOperation.GenerateContent ||
                             item.Operation == GenerationJobOperation.GenerateVocabulary ||
                             item.Operation == GenerationJobOperation.GenerateQuiz ||
                             item.Operation == GenerationJobOperation.GenerateDiscussion ||
                             item.Operation == GenerationJobOperation.GenerateListeningInteraction) &&
                            item.Status == GenerationJobStatus.Failed,
                    cancellationToken: cancellationToken))
                .OrderByDescending(item => item.Id)
                .FirstOrDefault();
            if (failed is null || !failed.GenerationRequestId.HasValue ||
                failed.Operation != GenerationJobOperation.GenerateContent)
            {
                throw new ConflictException("Story không có content generation thất bại hợp lệ để retry.");
            }

            await _unitOfWork.Repository<StoryGenerationJob>().AddAsync(new StoryGenerationJob
            {
                StoryId = storyId,
                GenerationRequestId = failed.GenerationRequestId,
                // A content retry produces a new candidate, not the failed version.
                // Reusing that version violates the unique operation/version index.
                StoryVersionId = null,
                BaseStoryVersionId = outline.Id,
                RequestedByUserId = userId,
                OperationKey = key,
                Operation = failed.Operation,
                Stage = failed.Operation == GenerationJobOperation.GenerateContent ? JobStage.ContentPending : JobStage.ContentArtifactPending,
                Status = GenerationJobStatus.Pending,
                AttemptNo = 0,
                MaxAttempts = failed.MaxAttempts > 0 ? failed.MaxAttempts : 3,
                StartedAt = DateTime.UtcNow
            }, cancellationToken);
            return true;
        }, cancellationToken);

        return await GetProgressAsync(userId, storyId, cancellationToken);
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var job = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(
            item => (item.Operation == GenerationJobOperation.GenerateContent ||
                     item.Operation == GenerationJobOperation.GenerateVocabulary ||
                     item.Operation == GenerationJobOperation.GenerateQuiz ||
                     item.Operation == GenerationJobOperation.GenerateDiscussion ||
                     item.Operation == GenerationJobOperation.GenerateListeningInteraction) &&
                    (item.AttemptNo < item.MaxAttempts ||
                     item.Operation == GenerationJobOperation.GenerateContent && item.Stage == JobStage.ContentStable) &&
                    (item.Status == GenerationJobStatus.Pending ||
                     (item.Status == GenerationJobStatus.Processing && item.LeaseExpiresAt < now)),
            cancellationToken: cancellationToken);
        if (job is null) return false;

        job.Status = GenerationJobStatus.Processing;
        job.Stage = job.Operation == GenerationJobOperation.GenerateContent && job.Stage == JobStage.ContentStable
            ? JobStage.ContentStable : job.Operation == GenerationJobOperation.GenerateContent
            ? JobStage.ContentGenerating : JobStage.ContentArtifactGenerating;
        job.AttemptNo++;
        job.StartedAt = now;
        job.LeaseExpiresAt = now.Add(_jobLease);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(job.StoryId, cancellationToken);
            _unitOfWork.Repository<StoryGenerationJob>().Update(job);
            RotateToken(job);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            return false;
        }

        var claimedToken = job.ConcurrencyToken;
        _logger.LogInformation("Generation job started JobId={JobId} StoryId={StoryId} VersionId={VersionId} Operation={Operation} Attempt={Attempt}",
            job.Id, job.StoryId, job.StoryVersionId ?? job.BaseStoryVersionId, job.Operation, job.AttemptNo);
        try
        {
            switch (job.Operation)
            {
                case GenerationJobOperation.GenerateContent:
                    await ProcessContentAsync(job.Id, claimedToken, cancellationToken);
                    break;
                case GenerationJobOperation.GenerateVocabulary:
                    await ProcessVocabularyAsync(job.Id, claimedToken, cancellationToken);
                    break;
                case GenerationJobOperation.GenerateQuiz:
                    await ProcessQuizAsync(job.Id, claimedToken, cancellationToken);
                    break;
                case GenerationJobOperation.GenerateDiscussion:
                    await ProcessDiscussionAsync(job.Id, claimedToken, cancellationToken);
                    break;
                case GenerationJobOperation.GenerateListeningInteraction:
                    await ProcessListeningAsync(job.Id, claimedToken, cancellationToken);
                    break;
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            var errorCode = PublicErrorCode(exception, job.Operation);
            // Provider exception messages may contain story text. Log only fixed codes and identifiers.
            var validationCode = LearningValidationFeedback.Normalize(exception.Data["ValidationCode"] as string ?? exception.Message);
            _logger.LogError("Generation job failed JobId={JobId} StoryId={StoryId} VersionId={VersionId} Operation={Operation} Attempt={Attempt} ErrorCode={ErrorCode} ValidationCode={ValidationCode} ItemIndex={ItemIndex} ExceptionType={ExceptionType}",
                job.Id, job.StoryId, job.StoryVersionId ?? job.BaseStoryVersionId, job.Operation, job.AttemptNo,
                IsCode(errorCode) ? errorCode : "UNCLASSIFIED_ERROR", validationCode,
                exception.Data["LearningItemIndex"] is int index ? index : null, exception.GetType().Name);
            await _failureFinalizer.MarkFailedAsync(job.Id, claimedToken, errorCode, CancellationToken.None);
        }
        return true;
    }

    private async Task ProcessContentAsync(int jobId, string claimedToken, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(_contentExecutionBudgetSeconds - 5));
        try
        {
            await ProcessContentWithinBudgetAsync(jobId, claimedToken, budget.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested &&
            (budget.IsCancellationRequested || ex.InnerException is TimeoutException))
        {
            throw new InvalidOperationException("CONTENT_GENERATION_TIMEOUT");
        }
        catch (TimeoutException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("CONTENT_GENERATION_TIMEOUT");
        }
        catch (AIServiceRequestException ex) when (!cancellationToken.IsCancellationRequested && ex.ErrorCode == "AI_TIMEOUT")
        {
            throw new InvalidOperationException("CONTENT_GENERATION_TIMEOUT");
        }
        // Promotion remains recoverable until the idempotent artifact handoff commits.
        using var completion = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        completion.CancelAfter(TimeSpan.FromSeconds(5));
        try { await CompleteStableHandoffAsync(jobId, claimedToken, completion.Token); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        { throw new InvalidOperationException("CONTENT_ARTIFACT_HANDOFF_FAILED"); }
    }

    private async Task ProcessContentWithinBudgetAsync(int jobId, string claimedToken, CancellationToken cancellationToken)
    {
        if (await HasPromotedContentAsync(jobId, cancellationToken)) return;
        var timer = Stopwatch.StartNew();
        var handoff = await LoadContentHandoffAsync(jobId, cancellationToken);
        StoryVersion? persistedCandidate = null;
        if (handoff.Job.StoryVersionId.HasValue && handoff.Job.StoryVersionId != handoff.BaseVersion.Id)
        {
            var existing = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(handoff.Job.StoryVersionId.Value, cancellationToken);
            if (existing is not null && existing.StoryId == handoff.Job.StoryId && !existing.IsCurrent && !string.IsNullOrWhiteSpace(existing.Content))
                persistedCandidate = existing;
        }

        StoryContentDto storyContent;
        GenerationMetadataDto metadata;
        var refinement = 0;
        if (persistedCandidate is null)
        {
            var response = await _aiClient.GenerateStoryContentAsync(
                BuildContentRequest(handoff.Job, handoff.BaseVersion, handoff.Input, handoff.Context) with
                { ExecutionBudgetSeconds = RemainingCallBudget(timer, 90) }, cancellationToken);
            storyContent = response.Story;
            metadata = response.Metadata;
        }
        else
        {
            storyContent = ToContent(persistedCandidate);
            var candidateEnvelope = string.IsNullOrWhiteSpace(handoff.Job.GenerationMetadataJson)
                ? null
                : JsonSerializer.Deserialize<CandidateMetadataEnvelope>(handoff.Job.GenerationMetadataJson, JsonOptions);
            storyContent = storyContent with { Description = candidateEnvelope?.Description };
            metadata = candidateEnvelope?.Generation ?? new GenerationMetadataDto();
            refinement = (await _unitOfWork.Repository<StoryVersion>().FindAsync(
                    item => item.StoryId == handoff.Job.StoryId && item.Content != null && item.EditType == VersionEditType.AiRefined,
                    cancellationToken: cancellationToken)).Count;
        }

        for (; ; )
        {
            var quality = _qualityEvaluator.Evaluate(storyContent, ToOutline(handoff.BaseVersion), handoff.Input, handoff.Context);
            if (quality.Safety.Passed)
            {
                var semanticSafety = await _aiClient.EvaluateContentSafetyAsync(new EvaluateContentSafetyRequest
                {
                    ExecutionBudgetSeconds = RemainingCallBudget(timer, 30),
                    RequestId = $"content-job-{handoff.Job.Id}-safety-{refinement}",
                    Story = storyContent,
                    AgeBand = handoff.Context.AgeBand,
                    Language = handoff.Context.Language,
                    BlockedTopics = PolicyTerms(handoff.Context.BlockedCategoryTerms, handoff.Context.BlockedCategoryCodes),
                    RestrictedTopics = PolicyTerms(handoff.Context.RestrictedCategoryTerms, handoff.Context.RestrictedCategoryCodes)
                }, cancellationToken);
                var safetyScore = (decimal)(semanticSafety.SafetyScore ?? (semanticSafety.IsAllowed ? 100d : 0d));
                var meetsThreshold = !handoff.Context.SafetyScoreThreshold.HasValue ||
                                     safetyScore >= handoff.Context.SafetyScoreThreshold.Value;
                if (!semanticSafety.IsAllowed || !meetsThreshold)
                {
                    var violations = semanticSafety.Violations.Count > 0
                        ? semanticSafety.Violations
                        : [!meetsThreshold
                            ? $"Safety score {safetyScore:F2} thấp hơn ngưỡng {handoff.Context.SafetyScoreThreshold:F2}."
                            : "Semantic safety evaluation rejected the generated story."];
                    quality = quality with
                    {
                        IsPassed = false,
                        SafetyScore = safetyScore,
                        Safety = new ContentQualityGate(false, semanticSafety.CanRefine,
                            !meetsThreshold ? "CONTENT_SAFETY_SCORE_NOT_MET" :
                            string.IsNullOrWhiteSpace(semanticSafety.ReasonCode) ? "CONTENT_SAFETY_BLOCKED" : semanticSafety.ReasonCode,
                            violations)
                    };
                }
                else
                {
                    quality = quality with { SafetyScore = safetyScore };
                }
            }
            var measuredWords = StoryWordCounter.Count(string.Join(' ', storyContent.StorySections.OrderBy(x => x.Order).Select(x => x.Content)));
            _logger.LogInformation("Content candidate JobId={JobId} StoryId={StoryId} Refinement={Refinement} WordCount={WordCount} ElapsedMs={ElapsedMs} FailedGates={FailedGates}",
                jobId, handoff.Job.StoryId, refinement, measuredWords, timer.ElapsedMilliseconds,
                string.Join(',', new[] { quality.Length, quality.Readability, quality.Safety, quality.OutlineConsistency, quality.VocabularyCompliance }
                    .Where(x => !x.Passed).Select(x => x.ReasonCode)));
            GrayZoneAssessment? moderationResult = null;
            if (_moderation?.IsConfigured == true && _grayZone is not null && quality.Safety.Passed)
            {
                var scan = new GrayZoneScan("phase3", handoff.Context.AgeBand, handoff.Context.Language,
                    GrayZoneEvaluator.TextParts(storyContent.Title, Flatten(storyContent)).Concat(
                        [new ModerationText("section", "lesson", storyContent.Lesson)]).ToArray(),
                    PolicyTerms(handoff.Context.BlockedCategoryTerms, handoff.Context.BlockedCategoryCodes).ToArray())
                { ReadingLevel = handoff.Context.ReadingLevel };
                var priorModeration = persistedCandidate is null ? null : GrayZoneWorkflowService.ReadAssessment(persistedCandidate);
                moderationResult = priorModeration is not null && priorModeration.Fingerprint == GrayZoneEvaluator.Fingerprint(scan)
                    && await _moderation.CanReuseAsync(priorModeration, cancellationToken) ? priorModeration
                    : await _moderation.EvaluateAsync(scan, cancellationToken);
                if (moderationResult.Zone == ModerationZone.HardBlock)
                    quality = quality with
                    {
                        IsPassed = false,
                        Safety = new ContentQualityGate(false, moderationResult.Provider != "rule-based", "MODERATION_HARD_BLOCK",
                            moderationResult.Findings.Select(x => x.Category + ": " + x.Reason).ToArray())
                    };
            }
            var candidate = persistedCandidate ?? await PersistCandidateAsync(
                handoff, storyContent, quality, refinement == 0 ? VersionEditType.Initial : VersionEditType.AiRefined,
                metadata, refinement, claimedToken, cancellationToken);
            persistedCandidate = null;
            if (moderationResult is not null)
            {
                var waitingForReview = await _grayZone!.RecordAsync(handoff.Job.StoryId, candidate.Id, jobId,
                    moderationResult, quality.IsPassed, cancellationToken, claimedToken,
                    quality.IsPassed || refinement >= _maxRefinementAttempts || quality.RefinementReasons.Count == 0);
                if (waitingForReview) return;
            }
            if (quality.IsPassed)
            {
                await PromoteStableContentAsync(handoff, candidate, storyContent, claimedToken, cancellationToken);
                return;
            }

            if (!quality.Safety.Passed && !quality.Safety.CanRefine)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(quality.Safety.ReasonCode)
                        ? "CONTENT_SAFETY_BLOCKED"
                        : quality.Safety.ReasonCode);
            if (refinement >= _maxRefinementAttempts || quality.RefinementReasons.Count == 0)
                throw new InvalidOperationException("CONTENT_QUALITY_NOT_MET");

            refinement++;
            var refined = await _aiClient.RefineStoryContentAsync(new RefineStoryContentRequest
            {
                ExecutionBudgetSeconds = RemainingCallBudget(timer, 90),
                Feedback = new ContentRefinementFeedback
                {
                    CurrentWordCount = measuredWords,
                    TargetWords = handoff.Input.TargetLength,
                    MinimumWords = handoff.Context.LearningProfile?.MinimumWords ?? Math.Max(100, (int)Math.Floor(handoff.Input.TargetLength * .7)),
                    MaximumWords = Constraints(handoff.Context).MaximumWords,
                    FailedGateCodes = new[] { quality.Length, quality.Readability, quality.Safety, quality.OutlineConsistency, quality.VocabularyCompliance }
                        .Where(x => !x.Passed && x.CanRefine && x.ReasonCode is not null).Select(x => x.ReasonCode!).ToArray()
                },
                RequestId = $"content-job-{handoff.Job.Id}-refine-{refinement}",
                Story = storyContent,
                Outline = ToOutline(handoff.BaseVersion),
                AgeBand = handoff.Context.AgeBand,
                ReadingLevel = handoff.Context.ReadingLevel.ToString(),
                VocabularyLevel = handoff.Context.VocabularyLevel,
                Language = handoff.Context.Language,
                TargetLength = handoff.Input.TargetLength,
                Constraints = Constraints(handoff.Context),
                Reasons = quality.RefinementReasons
            }, cancellationToken);
            storyContent = refined.Story;
            metadata = refined.Metadata;
        }
    }

    private int RemainingCallBudget(Stopwatch timer, int maximumSeconds)
    {
        var remaining = _contentExecutionBudgetSeconds - 5 - timer.Elapsed.TotalSeconds;
        if (remaining <= 0) throw new TimeoutException("Content execution budget exhausted.");
        // The linked cancellation token still enforces the exact remaining fractional second.
        return Math.Min(maximumSeconds, (int)Math.Ceiling(remaining));
    }

    private async Task ProcessVocabularyAsync(int jobId, string claimedToken, CancellationToken cancellationToken)
    {
        var state = await LoadArtifactStateAsync(jobId, GenerationJobOperation.GenerateVocabulary, cancellationToken);
        GenerateVocabularyResponse? response = null;
        GeneratedVocabularyItemDto[] items = [];
        if (_learningBatches is not null && state.Context.LearningProfile is not null)
        {
            response = (await _learningBatches.GenerateAsync(state.Job, claimedToken, state.Version, state.Context, cancellationToken)).Vocabulary;
            items = response.Items.ToArray();
            if (items.Length < 5) throw new InvalidOperationException("VOCABULARY_SOURCE_INSUFFICIENT");
        }
        else for (var attempt = 1; attempt <= _artifactMaxAttempts; attempt++)
        {
            response = await _aiClient.GenerateVocabularyAsync(new GenerateVocabularyRequest
            {
                RequestId = $"vocabulary-job-{jobId}-attempt-{attempt}",
                Story = ToContent(state.Version),
                AgeBand = state.Context.AgeBand,
                ReadingLevel = state.Context.ReadingLevel.ToString(),
                VocabularyLevel = state.Context.VocabularyLevel,
                Language = state.Context.Language
            }, cancellationToken);
            items = response.Items.Where(item => !string.IsNullOrWhiteSpace(item.Term) && !string.IsNullOrWhiteSpace(item.Definition))
                .DistinctBy(item => item.Term.Trim(), StringComparer.OrdinalIgnoreCase).ToArray();
            if (items.Length > 0 && items.All(item => ContainsTerm(state.Version.Content!, item.Term)) &&
                items.All(item => IsArtifactSafe($"{item.Term} {item.Definition}", state.Context))) break;
            if (attempt == _artifactMaxAttempts) throw new InvalidOperationException("VOCABULARY_VALIDATION_FAILED");
        }

        await CompleteArtifactAsync(state, claimedToken, response!.Metadata, async () =>
        {
            if (!await _unitOfWork.Repository<StoryVocabulary>().ExistsAsync(item => item.StoryVersionId == state.Version.Id, cancellationToken))
            {
                await _unitOfWork.Repository<StoryVocabulary>().AddRangeAsync(items.Select(item => new StoryVocabulary
                {
                    StoryVersionId = state.Version.Id,
                    Term = item.Term.Trim(),
                    Definition = item.Definition.Trim(),
                    SourceQuote = string.IsNullOrEmpty(item.SourceQuote) ? null : item.SourceQuote,
                    SourceStart = string.IsNullOrEmpty(item.SourceQuote) ? null : ToAnchor(state.Version.Content!, item.SourceQuote).Start,
                    SourceEnd = string.IsNullOrEmpty(item.SourceQuote) ? null : ToAnchor(state.Version.Content!, item.SourceQuote).End,
                    Example = string.IsNullOrEmpty(item.Example) ? null : item.Example
                }), cancellationToken);
            }
        }, new NextArtifact(GenerationJobOperation.GenerateQuiz), cancellationToken);
    }

    private async Task ProcessQuizAsync(int jobId, string claimedToken, CancellationToken cancellationToken)
    {
        var state = await LoadArtifactStateAsync(jobId, GenerationJobOperation.GenerateQuiz, cancellationToken);
        var vocabulary = await _unitOfWork.Repository<StoryVocabulary>().FindAsync(
            item => item.StoryVersionId == state.Version.Id, cancellationToken: cancellationToken);
        if (vocabulary.Count == 0) throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        GenerateQuizResponse? response = null;
        IReadOnlyList<QuizItemSeed>? mapped = null;
        if (_learningBatches is not null && state.Context.LearningProfile is not null)
        {
            response = await _learningBatches.ReadQuizAsync(state.Job.StoryId, state.Version.Id, cancellationToken);
            mapped = ValidateAndMapQuiz(response.Items, state.Version.Content!, state.Context);
        }
        else for (var attempt = 1; attempt <= _artifactMaxAttempts; attempt++)
        {
            response = await _aiClient.GenerateQuizAsync(new GenerateQuizRequest
            {
                RequestId = $"quiz-job-{jobId}-attempt-{attempt}",
                Story = ToContent(state.Version),
                AgeBand = state.Context.AgeBand,
                Language = state.Context.Language,
                ComprehensionGoal = state.Context.ComprehensionGoal,
                ComprehensionThresholdPercent = state.Context.ComprehensionThresholdPercent,
                Vocabulary = vocabulary.Select(item => new GeneratedVocabularyItemDto(item.Term, item.Definition)).ToArray()
            }, cancellationToken);
            try
            {
                mapped = ValidateAndMapQuiz(response.Items, state.Version.Content!, state.Context);
                break;
            }
            catch (InvalidOperationException) when (attempt < _artifactMaxAttempts)
            {
                // Retry only the invalid quiz artifact; previous artifacts remain unchanged.
            }
        }
        if (mapped is null) throw new InvalidOperationException("QUIZ_VALIDATION_FAILED");

        await CompleteArtifactAsync(state, claimedToken, response!.Metadata, async () =>
        {
            if (!await _unitOfWork.Repository<QuizItem>().ExistsAsync(item => item.StoryVersionId == state.Version.Id, cancellationToken))
                await _unitOfWork.Repository<QuizItem>().AddRangeAsync(
                    mapped.Select(item => (QuizItem)(item with { StoryVersionId = state.Version.Id })), cancellationToken);
        }, ListeningNext(ListeningPlan(state.Context, ListeningBlockParser.Parse(state.Version.Content!)), null), cancellationToken);
    }

    // Job Discussion cũ chỉ được đóng để truyện sang ContentReview.
    private async Task ProcessDiscussionAsync(int jobId, string claimedToken, CancellationToken cancellationToken)
    {
        var state = await LoadArtifactStateAsync(jobId, GenerationJobOperation.GenerateDiscussion, cancellationToken);
        await CompleteArtifactAsync(state, claimedToken, new GenerationMetadataDto(), () => Task.CompletedTask, null, cancellationToken);
    }

    private async Task CompleteArtifactAsync(
        ArtifactState state, string claimedToken, GenerationMetadataDto metadata, Func<Task> persist,
        NextArtifact? next, CancellationToken cancellationToken)
    {
        await InStoryTransactionAsync(state.Job.StoryId, async () =>
        {
            var job = await RequireClaimedJobAsync(state.Job.Id, claimedToken, cancellationToken);
            var version = await _unitOfWork.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.Id == state.Version.Id && !x.IsDeleted, cancellationToken: cancellationToken);
            if (version is null || !version.IsCurrent || string.IsNullOrWhiteSpace(version.Content))
                throw new InvalidOperationException("STALE_BASE_VERSION");
            if (_learningBatches is not null && state.Context.LearningProfile is not null)
            {
                var story = await _unitOfWork.Repository<Story>().FirstOrDefaultAsync(x => x.Id == job.StoryId, cancellationToken: cancellationToken)
                    ?? throw new InvalidOperationException("STORY_NOT_FOUND");
                var root = job.Operation == GenerationJobOperation.GenerateVocabulary ? job :
                    await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.StoryVersionId == version.Id &&
                        x.Operation == GenerationJobOperation.GenerateVocabulary, cancellationToken: cancellationToken)
                        ?? throw new ConflictException("LEARNING_BATCH_INCOMPLETE");
                await _learningBatches.ValidateSourceAsync(root.Id, story, version, job.RequestedByUserId ?? story.AuthorUserId, cancellationToken);
            }
            await persist();
            job.Status = GenerationJobStatus.Completed;
            job.Stage = next is not null ? JobStage.ContentArtifactCompleted : JobStage.ContentPackageCompleted;
            job.CompletedAt = DateTime.UtcNow;
            job.LeaseExpiresAt = null;
            job.GenerationMetadataJson = JsonSerializer.Serialize(metadata, JsonOptions);
            RotateToken(job);
            _unitOfWork.Repository<StoryGenerationJob>().Update(job);
            if (next is not null)
                await EnsureNextJobAsync(job, version.Id, next, cancellationToken);
            else
            {
                var story = await _unitOfWork.Repository<Story>().GetByIdAsync(job.StoryId, cancellationToken)
                            ?? throw new InvalidOperationException("STORY_NOT_FOUND");
                GenerationStoryAccess.EnsureSupported(story);
                story.Status = StoryStatus.ContentReview;
                _unitOfWork.Repository<Story>().Update(story);
            }
            return true;
        }, cancellationToken);

        if (next is null && _reviewService is not null)
        {
            try
            {
                var autoStory = await _unitOfWork.Repository<Story>().FirstOrDefaultAsync(x => x.Id == state.Job.StoryId, cancellationToken: cancellationToken);
                if (autoStory?.EverGrayZone != true)
                    await _reviewService.EvaluateAndApplyAutoPublishAsync(state.Job.StoryId, cancellationToken);
            }
            catch
            {
                // Fallback to ContentReview if auto-publish encounters an issue or validation fails
            }
        }
    }

    private async Task<StoryVersion> PersistCandidateAsync(
        ContentHandoff handoff, StoryContentDto content, ContentQualityResult quality, VersionEditType editType,
        GenerationMetadataDto metadata, int refinementAttempts, string claimedToken, CancellationToken cancellationToken)
    {
        return await InStoryTransactionAsync(handoff.Job.StoryId, async () =>
        {
            await RequireClaimedJobAsync(handoff.Job.Id, claimedToken, cancellationToken);
            var baseVersion = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(handoff.BaseVersion.Id, cancellationToken);
            if (baseVersion is null || !baseVersion.IsCurrent || !baseVersion.OutlineApprovedAt.HasValue)
                throw new InvalidOperationException("STALE_BASE_VERSION");
            var versions = await _unitOfWork.Repository<StoryVersion>().FindAsync(
                item => item.StoryId == handoff.Job.StoryId, cancellationToken: cancellationToken);
            var storyText = Flatten(content);
            var readability = ReadabilityCalculator.Calculate(storyText, handoff.Context.Language);
            var candidate = new StoryVersion
            {
                StoryId = handoff.Job.StoryId,
                VersionNo = versions.Select(item => item.VersionNo).DefaultIfEmpty().Max() + 1,
                EditType = editType,
                Title = content.Title.Trim(),
                OutlineOpening = baseVersion.OutlineOpening,
                OutlineDevelopment = baseVersion.OutlineDevelopment,
                OutlineEnding = baseVersion.OutlineEnding,
                OutlineApprovedAt = baseVersion.OutlineApprovedAt,
                OutlineApprovedByUserId = baseVersion.OutlineApprovedByUserId,
                Content = storyText,
                Lesson = content.Lesson.Trim(),
                ReadabilityFkgl = readability.Fkgl,
                ReadabilityFre = readability.Fre,
                SafetyScore = quality.SafetyScore ?? (quality.Safety.Passed ? 1m : 0m),
                IsCurrent = false
            };
            await _unitOfWork.Repository<StoryVersion>().AddAsync(candidate, cancellationToken);
            var job = await _unitOfWork.Repository<StoryGenerationJob>().GetByIdAsync(handoff.Job.Id, cancellationToken);
            if (job is not null)
            {
                job.BaseStoryVersionId ??= handoff.BaseVersion.Id;
                job.StoryVersionId = candidate.Id;
                job.StoryVersion = candidate;
                job.GenerationMetadataJson = JsonSerializer.Serialize(
                    new { generation = metadata, quality, refinementAttempts, description = content.Description,
                        lastMeasuredWordCount = StoryWordCounter.Count(string.Join(' ', content.StorySections.OrderBy(x => x.Order).Select(x => x.Content))) }, JsonOptions);
                _unitOfWork.Repository<StoryGenerationJob>().Update(job);
            }
            return candidate;
        }, cancellationToken);
    }

    private async Task PromoteStableContentAsync(
        ContentHandoff handoff,
        StoryVersion candidate,
        StoryContentDto content,
        string claimedToken,
        CancellationToken cancellationToken)
    {
        await InStoryTransactionAsync(handoff.Job.StoryId, async () =>
        {
            var job = await RequireClaimedJobAsync(handoff.Job.Id, claimedToken, cancellationToken);
            var current = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(handoff.BaseVersion.Id, cancellationToken);
            var stable = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(candidate.Id, cancellationToken);
            if (current is null || stable is null || !current.IsCurrent || !current.OutlineApprovedAt.HasValue)
                throw new InvalidOperationException("STALE_BASE_VERSION");
            current.IsCurrent = false;
            _unitOfWork.Repository<StoryVersion>().Update(current);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            stable.IsCurrent = true;
            stable.OutlineApprovedAt = current.OutlineApprovedAt;
            stable.OutlineApprovedByUserId = current.OutlineApprovedByUserId;
            _unitOfWork.Repository<StoryVersion>().Update(current);
            _unitOfWork.Repository<StoryVersion>().Update(stable);
            var story = await _unitOfWork.Repository<Story>().GetByIdAsync(job.StoryId, cancellationToken)
                        ?? throw new InvalidOperationException("STORY_NOT_FOUND");
            GenerationStoryAccess.EnsureSupported(story);
            story.Title = stable.Title;
            story.Content = stable.Content;
            story.MoralLesson = stable.Lesson;
            story.Description = NormalizeDescription(content.Description, stable.Content);
            _unitOfWork.Repository<Story>().Update(story);
            job.StoryVersionId = stable.Id;
            job.Status = GenerationJobStatus.Processing;
            job.Stage = JobStage.ContentStable;
            job.CompletedAt = null;
            _unitOfWork.Repository<StoryGenerationJob>().Update(job);

            return true;
        }, cancellationToken);

        // Handoff sang chuỗi Vocabulary → Quiz → tương tác lúc nghe qua entry point duy nhất.
        // QueueArtifactsAsync má»Ÿ transaction riÃªng, nÃªn gá»i sau khi transaction Content Ä‘Ã£ commit hoÃ n táº¥t.
    }

    private async Task EnsureNextJobAsync(
        StoryGenerationJob source, int stableVersionId, NextArtifact next, CancellationToken cancellationToken)
    {
        var operation = next.Operation;
        var listeningType = next.ListeningType;
        var existing = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(
            item => item.Operation == operation && item.StoryVersionId == stableVersionId &&
                    item.ListeningInteractionType == listeningType, cancellationToken: cancellationToken);
        if (existing is not null)
        {
            if (_learningBatches is not null && source.Operation == GenerationJobOperation.GenerateVocabulary &&
                operation == GenerationJobOperation.GenerateQuiz && existing.Status == GenerationJobStatus.Failed &&
                existing.ErrorCode == "QUIZ_VALIDATION_FAILED")
            {
                existing.Status = GenerationJobStatus.Pending; existing.AttemptNo = 0; existing.ErrorCode = null;
                existing.LeaseExpiresAt = existing.CompletedAt = null;
                // Attach with the database token before rotating it; otherwise EF uses the new token in WHERE.
                _unitOfWork.Repository<StoryGenerationJob>().Update(existing);
                RotateToken(existing);
                _unitOfWork.Repository<StoryGenerationJob>().Update(existing);
            }
            return;
        }
        var key = $"p3:{source.StoryId}:{stableVersionId}:{OperationName(operation)}";
        await _unitOfWork.Repository<StoryGenerationJob>().AddAsync(new StoryGenerationJob
        {
            StoryId = source.StoryId,
            GenerationRequestId = source.GenerationRequestId,
            StoryVersionId = stableVersionId,
            BaseStoryVersionId = stableVersionId,
            RequestedByUserId = source.RequestedByUserId,
            OperationKey = listeningType is null ? key : $"{key}:{listeningType}",
            Operation = operation,
            ListeningInteractionType = listeningType,
            Stage = JobStage.ContentArtifactPending,
            Status = GenerationJobStatus.Pending,
            AttemptNo = 0,
            MaxAttempts = operation == GenerationJobOperation.GenerateContent ? 3 : _artifactMaxAttempts,
            StartedAt = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task<ContentHandoff> LoadContentHandoffAsync(int jobId, CancellationToken cancellationToken)
    {
        var job = await _unitOfWork.Repository<StoryGenerationJob>().GetByIdAsync(jobId, cancellationToken)
                  ?? throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        var request = job.GenerationRequestId.HasValue
            ? await _unitOfWork.Repository<StoryGenerationRequest>().GetByIdAsync(job.GenerationRequestId.Value, cancellationToken)
            : null;
        var baseId = job.BaseStoryVersionId ?? job.StoryVersionId;
        var baseVersion = baseId.HasValue
            ? await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(baseId.Value, cancellationToken) : null;
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(job.StoryId, cancellationToken);
        if (request is null || request.Status != GenerationInputStatus.InputAccepted ||
            string.IsNullOrWhiteSpace(request.AcceptedInputJson) || string.IsNullOrWhiteSpace(request.ContextSnapshotJson) ||
            baseVersion is null || baseVersion.StoryId != job.StoryId || !baseVersion.IsCurrent ||
            !baseVersion.OutlineApprovedAt.HasValue || baseVersion.Content is not null ||
            story is null || story.Source != StorySource.Ai || story.Status != StoryStatus.OutlineReview)
            throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        var input = JsonSerializer.Deserialize<AcceptedAIStoryInputSnapshot>(request.AcceptedInputJson, JsonOptions)
                    ?? throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        var context = JsonSerializer.Deserialize<AIStoryInputContextSnapshot>(request.ContextSnapshotJson, JsonOptions)
                      ?? throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        GenerationStoryAccess.ValidateSnapshot(story!, context);
        return new ContentHandoff(job, request, baseVersion, input, context);
    }

    private async Task<ArtifactState> LoadArtifactStateAsync(
        int jobId, GenerationJobOperation operation, CancellationToken cancellationToken)
    {
        var job = await _unitOfWork.Repository<StoryGenerationJob>().GetByIdAsync(jobId, cancellationToken);
        if (job is null || job.Operation != operation || !job.StoryVersionId.HasValue || !job.GenerationRequestId.HasValue)
            throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        var version = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(job.StoryVersionId.Value, cancellationToken);
        var request = await _unitOfWork.Repository<StoryGenerationRequest>().GetByIdAsync(job.GenerationRequestId.Value, cancellationToken);
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(job.StoryId, cancellationToken);
        if (story?.OutputMode == StoryOutputMode.ReadingMediaOnly)
            throw new InvalidOperationException("LEARNING_ARTIFACT_NOT_ENABLED");
        if (version is null || !version.IsCurrent || string.IsNullOrWhiteSpace(version.Content) || request is null)
            throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        var context = JsonSerializer.Deserialize<AIStoryInputContextSnapshot>(request.ContextSnapshotJson, JsonOptions)
                      ?? throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        GenerationStoryAccess.ValidateSnapshot(story!, context);
        return new ArtifactState(job, version, context);
    }

    private async Task<StoryGenerationJob> RequireClaimedJobAsync(
        int jobId, string claimedToken, CancellationToken cancellationToken)
    {
        var job = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.Id == jobId && !x.IsDeleted, cancellationToken: cancellationToken);
        if (job is null || job.Status != GenerationJobStatus.Processing || job.ConcurrencyToken != claimedToken || job.LeaseExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException("STALE_JOB_RESULT");
        return job;
    }

    private async Task<Story> LoadAuthorizedStoryAsync(int userId, int storyId, CancellationToken cancellationToken)
    {
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story", storyId);
        GenerationStoryAccess.EnsureSupported(story);
        if (await GenerationStoryAccess.AuthorizeCreatorAsync(_unitOfWork, story, userId, cancellationToken)) return story;
        if (!await ChildOwnership.IsOwnerAsync(_unitOfWork, story.ChildProfileId, userId, cancellationToken))
            throw new ForbiddenException();
        return story;
    }

    private async Task<T> InStoryTransactionAsync<T>(int storyId, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, cancellationToken);
            var result = await action();
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return result;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private static GenerateStoryContentRequest BuildContentRequest(
        StoryGenerationJob job, StoryVersion version, AcceptedAIStoryInputSnapshot input, AIStoryInputContextSnapshot context) => new()
        {
            ExecutionBudgetSeconds = 90,
            RequestId = $"content-job-{job.Id}",
            ApprovedOutlineReference = $"story-version-{version.Id}",
            Outline = ToOutline(version),
            AgeBand = context.AgeBand,
            ReadingLevel = context.ReadingLevel.ToString(),
            VocabularyLevel = context.VocabularyLevel,
            Language = context.Language,
            ComprehensionGoal = context.ComprehensionGoal,
            StoryParameters = new StoryParametersDto
            {
                Genre = input.Genre,
                CharacterMode = input.CharacterMode,
                Characters = input.Characters,
                SettingMode = input.SettingMode,
                Setting = input.Setting ?? string.Empty,
                Topic = input.Topic,
                Lesson = input.Lesson,
                RequestedLength = input.TargetLength
            },
            Constraints = Constraints(context)
        };

    private static GenerationConstraintsDto Constraints(AIStoryInputContextSnapshot context) => new()
    {
        Readability = ContentReadabilityLimits.ForLevel(context.ReadingLevel, context.ReadabilityScoreThreshold),
        MinimumWords = context.LearningProfile?.MinimumWords ?? 0,
        MaximumWords = Math.Min(context.MaximumLength, context.LearningProfile?.MaximumWords ?? context.MaximumLength),
        AllowedTopics = PolicyTerms(context.AllowedCategoryTerms, context.AllowedCategoryCodes),
        RestrictedTopics = PolicyTerms(context.RestrictedCategoryTerms, context.RestrictedCategoryCodes),
        BlockedTopics = PolicyTerms(context.BlockedCategoryTerms, context.BlockedCategoryCodes)
    };

    private static StoryOutlineDto ToOutline(StoryVersion version) => new(
        version.OutlineOpening ?? string.Empty, version.OutlineDevelopment ?? string.Empty, version.OutlineEnding ?? string.Empty);
    private static StoryContentDto ToContent(StoryVersion version) => new()
    {
        Title = version.Title,
        StorySections = [new StorySectionDto(1, string.Empty, version.Content ?? string.Empty)],
        Lesson = !string.IsNullOrWhiteSpace(version.Lesson) ? version.Lesson : (!string.IsNullOrWhiteSpace(version.Title) ? version.Title : "Bài học từ câu chuyện")
    };
    private static string Flatten(StoryContentDto story) => string.Join("\n\n", story.StorySections.OrderBy(item => item.Order)
        .Select(item => string.IsNullOrWhiteSpace(item.Heading) ? item.Content.Trim() : $"{item.Heading.Trim()}\n{item.Content.Trim()}"));
    private static string NormalizeDescription(string? description, string? content)
    {
        var source = string.IsNullOrWhiteSpace(description) ? content : description;
        if (string.IsNullOrWhiteSpace(source)) return string.Empty;
        var normalized = string.Join(' ', source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= 300) return normalized;
        var cut = normalized.LastIndexOf(' ', 299);
        return normalized[..(cut >= 120 ? cut : 299)].TrimEnd() + "…";
    }
    private static IReadOnlyList<string> PolicyTerms(IReadOnlyList<string>? terms, IReadOnlyList<string> fallback) =>
        terms is { Count: > 0 } ? terms : fallback;
    private static bool ContainsTerm(string content, string term) => content.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase);
    private static void RotateToken(StoryGenerationJob job) => job.ConcurrencyToken = Guid.NewGuid().ToString("N");
    private static bool IsContentGenerationOperation(GenerationJobOperation operation) => operation is
        GenerationJobOperation.GenerateContent or GenerationJobOperation.GenerateVocabulary or
        GenerationJobOperation.GenerateQuiz or GenerationJobOperation.GenerateDiscussion or
        GenerationJobOperation.GenerateListeningInteraction;
    private static ContentQualityFailureDto? ToQualityFailure(StoryGenerationJob? failedJob)
    {
        if (failedJob is null || string.IsNullOrWhiteSpace(failedJob.GenerationMetadataJson)) return null;

        try
        {
            var envelope = JsonSerializer.Deserialize<CandidateMetadataEnvelope>(
                failedJob.GenerationMetadataJson, JsonOptions);
            if (envelope?.Quality is null) return null;

            var quality = envelope.Quality;
            var failedGates = new (string Name, ContentQualityGate Gate)[]
                {
                    ("outline_consistency", quality.OutlineConsistency),
                    ("length", quality.Length),
                    ("safety", quality.Safety),
                    ("readability", quality.Readability),
                    ("vocabulary_compliance", quality.VocabularyCompliance)
                }
                .Where(item => !item.Gate.Passed)
                .Select(item => new ContentQualityGateFailureDto
                {
                    Gate = item.Name,
                    ReasonCode = item.Gate.ReasonCode ?? "CONTENT_QUALITY_NOT_MET",
                    Violations = item.Gate.Violations
                })
                .ToArray();

            return failedGates.Length == 0 ? null : new ContentQualityFailureDto
            {
                RefinementAttempts = envelope.RefinementAttempts,
                SafetyScore = quality.SafetyScore,
                FailedGates = failedGates
            };
        }
        catch (JsonException)
        {
            // Progress remains available even when legacy or malformed metadata cannot be parsed.
            return null;
        }
    }
    private static void ValidateObject(object value)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(value, new ValidationContext(value), results, true))
        {
            throw new BadRequestException(string.Join(" ", results.Select(item => item.ErrorMessage)));
        }
    }
    private static string OperationName(GenerationJobOperation operation) => operation switch
    {
        GenerationJobOperation.GenerateContent => "content",
        GenerationJobOperation.GenerateVocabulary => "vocabulary",
        GenerationJobOperation.GenerateQuiz => "quiz",
        GenerationJobOperation.GenerateDiscussion => "discussion",
        GenerationJobOperation.GenerateListeningInteraction => "listening",
        _ => "unknown"
    };
    private static string JobState(IEnumerable<StoryGenerationJob> jobs, GenerationJobOperation operation)
    {
        var job = jobs.Where(item => item.Operation == operation).OrderByDescending(item => item.Id).FirstOrDefault();
        return job is null ? "not_started" : job.Status.ToString().ToLowerInvariant();
    }
    private static string PublicErrorCode(Exception exception, GenerationJobOperation operation) => exception switch
    {
        AIServiceRequestException ai => ai.ErrorCode,
        InvalidOperationException invalid when IsCode(invalid.Message) => invalid.Message,
        AppException app when IsCode(app.Message) => app.Message,
        JsonException => "CONTENT_SCHEMA_INVALID",
        _ => operation switch
        {
            GenerationJobOperation.GenerateVocabulary => "VOCABULARY_GENERATION_FAILED",
            GenerationJobOperation.GenerateQuiz => "QUIZ_GENERATION_FAILED",
            GenerationJobOperation.GenerateDiscussion => "DISCUSSION_GENERATION_FAILED",
            GenerationJobOperation.GenerateListeningInteraction => "LISTENING_GENERATION_FAILED",
            _ => "CONTENT_TECHNICAL_GENERATION_FAILED"
        }
    };
    private static bool IsCode(string value) => value.Length <= 100 && value.All(character => char.IsUpper(character) || char.IsDigit(character) || character == '_');
    private static string ToSnake(string value) => string.Concat(value.Select((character, index) =>
        char.IsUpper(character) && index > 0 ? $"_{char.ToLowerInvariant(character)}" : char.ToLowerInvariant(character).ToString()));

    private static IReadOnlyList<QuizItemSeed> ValidateAndMapQuiz(
        IReadOnlyList<QuizItemDto> items, string storyContent, AIStoryInputContextSnapshot context)
    {
        if (items.Count < 3 || items.Select(item => item.Type).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 3)
            throw QuizFailure("QUIZ_TYPE_COVERAGE_INVALID");
        var result = new List<QuizItemSeed>();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var quizText = string.Join(' ', new[] { item.Question, item.CorrectAnswer, item.Explanation }.Concat(item.Options));
            if (string.IsNullOrWhiteSpace(item.Question)) throw QuizFailure("QUIZ_QUESTION_REQUIRED", index);
            if (!IsArtifactSafe(quizText, context)) throw QuizFailure("LEARNING_SAFETY_INVALID", index);
            if (!HasRelevantTerm(item.Question, storyContent)) throw QuizFailure("QUIZ_QUESTION_RELEVANCE_INVALID", index);
            var type = item.Type.ToLowerInvariant() switch
            {
                "multiple_choice" => QuizType.MultipleChoice,
                "true_false" => QuizType.TrueFalse,
                "short_answer" => QuizType.ShortAnswer,
                _ => throw QuizFailure("QUIZ_ANSWER_INVALID", index)
            };
            var answer = item.CorrectAnswer;
            if (type == QuizType.MultipleChoice)
            {
                if (item.Options.Count < 2 || item.CorrectOptionIndex < 0 || item.CorrectOptionIndex >= item.Options.Count)
                    throw QuizFailure("QUIZ_OPTIONS_INVALID", index);
                answer = string.IsNullOrWhiteSpace(answer) ? item.Options[item.CorrectOptionIndex] : answer;
                if (!item.Options.Contains(answer, StringComparer.OrdinalIgnoreCase)) throw QuizFailure("QUIZ_OPTIONS_INVALID", index);
            }
            else if (type == QuizType.TrueFalse)
            {
                if (string.Equals(answer, "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(answer, "đúng", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(answer, "dung", StringComparison.OrdinalIgnoreCase))
                {
                    answer = "true";
                }
                else if (string.Equals(answer, "false", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(answer, "sai", StringComparison.OrdinalIgnoreCase))
                {
                    answer = "false";
                }
                else if (item.CorrectOptionIndex == 0)
                {
                    answer = "true";
                }
                else if (item.CorrectOptionIndex == 1)
                {
                    answer = "false";
                }
                else
                {
                    throw QuizFailure("QUIZ_TRUE_FALSE_INVALID", index);
                }
            }
            else if (type == QuizType.ShortAnswer && string.IsNullOrWhiteSpace(answer))
                throw QuizFailure("QUIZ_ANSWER_INVALID", index);
            var anchor = string.IsNullOrEmpty(item.SourceQuote) ? ((int?)null, (int?)null) : ToAnchor(storyContent, item.SourceQuote);
            result.Add(new QuizItemSeed(type, item.Question.Trim(), answer.Trim(), JsonSerializer.Serialize(item.Options, JsonOptions))
                { Detail = item, SourceStart = anchor.Item1, SourceEnd = anchor.Item2 });
        }
        return result;
    }

    private static InvalidOperationException QuizFailure(string code, int? index = null)
    {
        var error = new InvalidOperationException("QUIZ_VALIDATION_FAILED");
        error.Data["ValidationCode"] = code;
        if (index.HasValue) error.Data["LearningItemIndex"] = index.Value;
        return error;
    }

    private static (int Start, int End) ToAnchor(string content, string quote)
    {
        if (LearningArtifactGrounding.TryAnchor(content, quote, out var range)) return range;
        // Learning batches are validated against the trimmed canonical story, so retry on that basis and add the
        // leading offset back; otherwise a quoted first line would land at the wrong offset in the stored content.
        var trimmed = content.TrimStart();
        var offset = content.Length - trimmed.Length;
        if (LearningArtifactGrounding.TryAnchor(trimmed, quote, out var shifted))
            return (shifted.Start + offset, shifted.End + offset);
        throw new JsonException("LEARNING_EVIDENCE_NOT_IN_STORY");
    }

    private static bool IsArtifactSafe(string value, AIStoryInputContextSnapshot context) =>
        ListeningInteractionGenerator.IsSafe(value, context);

    private static bool HasRelevantTerm(string value, string source) => LearningArtifactGrounding.HasRelevantTerm(value, source);

    private sealed record ContentHandoff(StoryGenerationJob Job, StoryGenerationRequest Request, StoryVersion BaseVersion,
        AcceptedAIStoryInputSnapshot Input, AIStoryInputContextSnapshot Context);
    private sealed record CandidateMetadataEnvelope(
        GenerationMetadataDto? Generation,
        ContentQualityResult? Quality,
        int RefinementAttempts = 0,
        string? Description = null,
        int? LastMeasuredWordCount = null);
    private sealed record ArtifactState(StoryGenerationJob Job, StoryVersion Version, AIStoryInputContextSnapshot Context);
    private sealed record NextArtifact(GenerationJobOperation Operation, ListeningInteractionType? ListeningType = null);
    private sealed record QuizItemSeed(QuizType Type, string Question, string CorrectAnswer, string Choices)
    {
        public StoryPlatform.Contracts.AI.Models.QuizItemDto? Detail { get; init; }
        public int? SourceStart { get; init; }
        public int? SourceEnd { get; init; }
        public int StoryVersionId { get; init; }
        public static implicit operator QuizItem(QuizItemSeed item) => new()
        {
            StoryVersionId = item.StoryVersionId,
            Type = item.Type,
            Question = item.Question,
            CorrectAnswer = item.CorrectAnswer,
            Choices = item.Choices,
            Explanation = item.Detail?.Explanation, Hint = item.Detail?.Hint,
            LearningGoal = item.Detail?.LearningGoal, SourceQuote = item.Detail?.SourceQuote,
            SourceStart = item.SourceStart, SourceEnd = item.SourceEnd,
            OptionFeedbackJson = item.Detail is null ? null : JsonSerializer.Serialize(item.Detail.OptionFeedback, JsonOptions)
        };
    }
}
