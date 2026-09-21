using System.Text.Json;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.ContentGeneration;
using StoryPlatform.Application.Features.ContentGeneration.DTOs;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration.Quality;
using StoryPlatform.Application.Features.ExistingStories.Interfaces;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentGeneration.Services;

public sealed class ContentGenerationService : IContentGenerationService, IContentGenerationJobProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAIStoryGenerationClient _aiClient;
    private readonly IContentQualityEvaluator _qualityEvaluator;
    private readonly IContentGenerationJobFailureFinalizer _failureFinalizer;
    private readonly IStableVersionArtifactHandoffService _artifactHandoff;
    private readonly int _maxRefinementAttempts;
    private readonly int _artifactMaxAttempts;
    private readonly TimeSpan _jobLease;

    public ContentGenerationService(
        IUnitOfWork unitOfWork,
        IAIStoryGenerationClient aiClient,
        IContentQualityEvaluator qualityEvaluator,
        IContentGenerationJobFailureFinalizer failureFinalizer,
        IStableVersionArtifactHandoffService artifactHandoff,
        ContentGenerationOptions options)
    {
        _unitOfWork = unitOfWork;
        _aiClient = aiClient;
        _qualityEvaluator = qualityEvaluator;
        _failureFinalizer = failureFinalizer;
        _artifactHandoff = artifactHandoff;
        _maxRefinementAttempts = Math.Clamp(options.MaxContentRefinementAttempts, 0, 2);
        _artifactMaxAttempts = Math.Clamp(options.ArtifactMaxAttempts, 1, 3);
        _jobLease = TimeSpan.FromMinutes(Math.Clamp(options.JobLeaseMinutes, 1, 30));
    }

    public async Task<ContentGenerationProgressDto> GetProgressAsync(
        int userId, int storyId, CancellationToken cancellationToken = default)
    {
        var story = await LoadAuthorizedStoryAsync(userId, storyId, cancellationToken);
        var jobs = await _unitOfWork.Repository<StoryGenerationJob>().FindAsync(
            item => item.StoryId == storyId &&
                    (item.Operation == GenerationJobOperation.GenerateContent ||
                     item.Operation == GenerationJobOperation.GenerateVocabulary ||
                     item.Operation == GenerationJobOperation.GenerateQuiz ||
                     item.Operation == GenerationJobOperation.GenerateDiscussion),
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
        var discussionDone = stable is not null && await _unitOfWork.Repository<DiscussionQuestion>()
            .ExistsAsync(item => item.StoryVersionId == stable.Id, cancellationToken);
        var active = jobs.Where(item => item.Status is GenerationJobStatus.Pending or GenerationJobStatus.Processing)
            .OrderByDescending(item => item.Id).FirstOrDefault();
        var failed = jobs.Where(item => item.Status == GenerationJobStatus.Failed)
            .OrderByDescending(item => item.Id).FirstOrDefault();

        return new ContentGenerationProgressDto
        {
            StoryId = story.Id,
            StoryStatus = ToSnake(story.Status.ToString()),
            CurrentStep = story.Status == StoryStatus.ContentReview ? "complete" : active is null ? "not_started" :
                $"{(active.Status == GenerationJobStatus.Processing ? "generating" : "pending")}_{OperationName(active.Operation)}",
            Content = stable is not null ? "stable" : JobState(jobs, GenerationJobOperation.GenerateContent),
            Vocabulary = vocabularyDone ? "completed" : JobState(jobs, GenerationJobOperation.GenerateVocabulary),
            Quiz = quizDone ? "completed" : JobState(jobs, GenerationJobOperation.GenerateQuiz),
            Discussion = discussionDone ? "completed" : JobState(jobs, GenerationJobOperation.GenerateDiscussion),
            IsComplete = story.Status == StoryStatus.ContentReview && stable is not null && vocabularyDone && quizDone && discussionDone,
            LastErrorCode = failed?.ErrorCode,
            StableStoryVersionId = stable?.Id
        };
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var job = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(
            item => (item.Operation == GenerationJobOperation.GenerateContent ||
                     item.Operation == GenerationJobOperation.GenerateVocabulary ||
                     item.Operation == GenerationJobOperation.GenerateQuiz ||
                     item.Operation == GenerationJobOperation.GenerateDiscussion) &&
                    item.AttemptNo < item.MaxAttempts &&
                    (item.Status == GenerationJobStatus.Pending ||
                     (item.Status == GenerationJobStatus.Processing && item.LeaseExpiresAt < now)),
            cancellationToken: cancellationToken);
        if (job is null) return false;

        job.Status = GenerationJobStatus.Processing;
        job.Stage = job.Operation == GenerationJobOperation.GenerateContent
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
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            await _failureFinalizer.MarkFailedAsync(job.Id, claimedToken, PublicErrorCode(exception, job.Operation), CancellationToken.None);
        }
        return true;
    }

    private async Task ProcessContentAsync(int jobId, string claimedToken, CancellationToken cancellationToken)
    {
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
                BuildContentRequest(handoff.Job, handoff.BaseVersion, handoff.Input, handoff.Context), cancellationToken);
            storyContent = response.Story;
            metadata = response.Metadata;
        }
        else
        {
            storyContent = ToContent(persistedCandidate);
            metadata = string.IsNullOrWhiteSpace(handoff.Job.GenerationMetadataJson)
                ? new GenerationMetadataDto()
                : JsonSerializer.Deserialize<CandidateMetadataEnvelope>(handoff.Job.GenerationMetadataJson, JsonOptions)?.Generation
                  ?? new GenerationMetadataDto();
            refinement = (await _unitOfWork.Repository<StoryVersion>().FindAsync(
                    item => item.StoryId == handoff.Job.StoryId && item.Content != null && item.EditType == VersionEditType.AiRefined,
                    cancellationToken: cancellationToken)).Count;
        }

        for (;;)
        {
            var quality = _qualityEvaluator.Evaluate(storyContent, ToOutline(handoff.BaseVersion), handoff.Input, handoff.Context);
            if (quality.Safety.Passed)
            {
                var semanticSafety = await _aiClient.EvaluateContentSafetyAsync(new EvaluateContentSafetyRequest
                {
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
            var candidate = persistedCandidate ?? await PersistCandidateAsync(
                handoff, storyContent, quality, refinement == 0 ? VersionEditType.Initial : VersionEditType.AiRefined,
                metadata, claimedToken, cancellationToken);
            persistedCandidate = null;
            if (quality.IsPassed)
            {
                await PromoteStableContentAsync(handoff, candidate, claimedToken, cancellationToken);
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

    private async Task ProcessVocabularyAsync(int jobId, string claimedToken, CancellationToken cancellationToken)
    {
        var state = await LoadArtifactStateAsync(jobId, GenerationJobOperation.GenerateVocabulary, cancellationToken);
        GenerateVocabularyResponse? response = null;
        GeneratedVocabularyItemDto[] items = [];
        for (var attempt = 1; attempt <= _artifactMaxAttempts; attempt++)
        {
            response = await _aiClient.GenerateVocabularyAsync(new GenerateVocabularyRequest
            {
                RequestId = $"vocabulary-job-{jobId}-attempt-{attempt}", Story = ToContent(state.Version), AgeBand = state.Context.AgeBand,
                ReadingLevel = state.Context.ReadingLevel.ToString(), VocabularyLevel = state.Context.VocabularyLevel,
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
                    StoryVersionId = state.Version.Id, Term = item.Term.Trim(), Definition = item.Definition.Trim()
                }), cancellationToken);
            }
        }, GenerationJobOperation.GenerateQuiz, cancellationToken);
    }

    private async Task ProcessQuizAsync(int jobId, string claimedToken, CancellationToken cancellationToken)
    {
        var state = await LoadArtifactStateAsync(jobId, GenerationJobOperation.GenerateQuiz, cancellationToken);
        var vocabulary = await _unitOfWork.Repository<StoryVocabulary>().FindAsync(
            item => item.StoryVersionId == state.Version.Id, cancellationToken: cancellationToken);
        if (vocabulary.Count == 0) throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        GenerateQuizResponse? response = null;
        IReadOnlyList<QuizItemSeed>? mapped = null;
        for (var attempt = 1; attempt <= _artifactMaxAttempts; attempt++)
        {
            response = await _aiClient.GenerateQuizAsync(new GenerateQuizRequest
            {
                RequestId = $"quiz-job-{jobId}-attempt-{attempt}", Story = ToContent(state.Version), AgeBand = state.Context.AgeBand,
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
        }, GenerationJobOperation.GenerateDiscussion, cancellationToken);
    }

    private async Task ProcessDiscussionAsync(int jobId, string claimedToken, CancellationToken cancellationToken)
    {
        var state = await LoadArtifactStateAsync(jobId, GenerationJobOperation.GenerateDiscussion, cancellationToken);
        if (!await _unitOfWork.Repository<StoryVocabulary>().ExistsAsync(item => item.StoryVersionId == state.Version.Id, cancellationToken) ||
            !await _unitOfWork.Repository<QuizItem>().ExistsAsync(item => item.StoryVersionId == state.Version.Id, cancellationToken))
            throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        GenerateDiscussionResponse? response = null;
        string[] questions = [];
        for (var attempt = 1; attempt <= _artifactMaxAttempts; attempt++)
        {
            response = await _aiClient.GenerateDiscussionAsync(new GenerateDiscussionRequest
            {
                RequestId = $"discussion-job-{jobId}-attempt-{attempt}", Story = ToContent(state.Version), AgeBand = state.Context.AgeBand,
                Language = state.Context.Language,
                ComprehensionGoal = state.Context.ComprehensionGoal
            }, cancellationToken);
            questions = response.Items.Select(item => item.Question.Trim()).Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (questions.Length > 0 && questions.All(item => IsArtifactSafe(item, state.Context)) &&
                questions.All(item => HasRelevantTerm(item, $"{state.Version.Content} {state.Version.Lesson}"))) break;
            if (attempt == _artifactMaxAttempts) throw new InvalidOperationException("DISCUSSION_VALIDATION_FAILED");
        }

        await CompleteArtifactAsync(state, claimedToken, response!.Metadata, async () =>
        {
            if (!await _unitOfWork.Repository<DiscussionQuestion>().ExistsAsync(item => item.StoryVersionId == state.Version.Id, cancellationToken))
                await _unitOfWork.Repository<DiscussionQuestion>().AddRangeAsync(questions.Select(question => new DiscussionQuestion
                {
                    StoryVersionId = state.Version.Id, Question = question, IsMoralLesson = false
                }), cancellationToken);
        }, null, cancellationToken);
    }

    private async Task CompleteArtifactAsync(
        ArtifactState state, string claimedToken, GenerationMetadataDto metadata, Func<Task> persist,
        GenerationJobOperation? nextOperation, CancellationToken cancellationToken)
    {
        await InStoryTransactionAsync(state.Job.StoryId, async () =>
        {
            var job = await RequireClaimedJobAsync(state.Job.Id, claimedToken, cancellationToken);
            var version = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(state.Version.Id, cancellationToken);
            if (version is null || !version.IsCurrent || string.IsNullOrWhiteSpace(version.Content))
                throw new InvalidOperationException("STALE_BASE_VERSION");
            await persist();
            job.Status = GenerationJobStatus.Completed;
            job.Stage = nextOperation.HasValue ? JobStage.ContentArtifactCompleted : JobStage.ContentPackageCompleted;
            job.CompletedAt = DateTime.UtcNow;
            job.LeaseExpiresAt = null;
            job.GenerationMetadataJson = JsonSerializer.Serialize(metadata, JsonOptions);
            RotateToken(job);
            _unitOfWork.Repository<StoryGenerationJob>().Update(job);
            if (nextOperation.HasValue)
                await EnsureNextJobAsync(job, version.Id, nextOperation.Value, cancellationToken);
            else
            {
                var story = await _unitOfWork.Repository<Story>().GetByIdAsync(job.StoryId, cancellationToken)
                            ?? throw new InvalidOperationException("STORY_NOT_FOUND");
                story.Status = StoryStatus.ContentReview;
                _unitOfWork.Repository<Story>().Update(story);
            }
            return true;
        }, cancellationToken);
    }

    private async Task<StoryVersion> PersistCandidateAsync(
        ContentHandoff handoff, StoryContentDto content, ContentQualityResult quality, VersionEditType editType,
        GenerationMetadataDto metadata, string claimedToken, CancellationToken cancellationToken)
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
                job.GenerationMetadataJson = JsonSerializer.Serialize(new { generation = metadata, quality }, JsonOptions);
                _unitOfWork.Repository<StoryGenerationJob>().Update(job);
            }
            return candidate;
        }, cancellationToken);
    }

    private async Task PromoteStableContentAsync(
        ContentHandoff handoff, StoryVersion candidate, string claimedToken, CancellationToken cancellationToken)
    {
        await InStoryTransactionAsync(handoff.Job.StoryId, async () =>
        {
            var job = await RequireClaimedJobAsync(handoff.Job.Id, claimedToken, cancellationToken);
            var current = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(handoff.BaseVersion.Id, cancellationToken);
            var stable = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(candidate.Id, cancellationToken);
            if (current is null || stable is null || !current.IsCurrent || !current.OutlineApprovedAt.HasValue)
                throw new InvalidOperationException("STALE_BASE_VERSION");
            current.IsCurrent = false;
            stable.IsCurrent = true;
            _unitOfWork.Repository<StoryVersion>().Update(current);
            _unitOfWork.Repository<StoryVersion>().Update(stable);
            var story = await _unitOfWork.Repository<Story>().GetByIdAsync(job.StoryId, cancellationToken)
                        ?? throw new InvalidOperationException("STORY_NOT_FOUND");
            story.Title = stable.Title;
            story.Content = stable.Content;
            story.MoralLesson = stable.Lesson;
            _unitOfWork.Repository<Story>().Update(story);
            job.StoryVersionId = stable.Id;
            job.Status = GenerationJobStatus.Completed;
            job.Stage = JobStage.ContentStable;
            job.CompletedAt = DateTime.UtcNow;
            job.LeaseExpiresAt = null;
            RotateToken(job);
            _unitOfWork.Repository<StoryGenerationJob>().Update(job);

            // Handoff sang chuỗi Vocabulary → Quiz → Discussion qua entry point duy nhất.
            // StableVersionArtifactHandoffService idempotency: nếu job đã có cho (story, version)
            // thì trả về job hiện có, không tạo trùng.
            await _artifactHandoff.QueueArtifactsAsync(
                storyId: job.StoryId,
                storyVersionId: stable.Id,
                requestedByUserId: job.RequestedByUserId ?? story.AuthorUserId,
                generationRequestId: job.GenerationRequestId,
                cancellationToken: cancellationToken);
            return true;
        }, cancellationToken);
    }

    private async Task EnsureNextJobAsync(
        StoryGenerationJob source, int stableVersionId, GenerationJobOperation operation, CancellationToken cancellationToken)
    {
        if (await _unitOfWork.Repository<StoryGenerationJob>().ExistsAsync(
                item => item.Operation == operation && item.StoryVersionId == stableVersionId, cancellationToken)) return;
        await _unitOfWork.Repository<StoryGenerationJob>().AddAsync(new StoryGenerationJob
        {
            StoryId = source.StoryId,
            GenerationRequestId = source.GenerationRequestId,
            StoryVersionId = stableVersionId,
            BaseStoryVersionId = stableVersionId,
            RequestedByUserId = source.RequestedByUserId,
            OperationKey = $"p3:{source.StoryId}:{stableVersionId}:{OperationName(operation)}",
            Operation = operation,
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
        ValidateConsentContext(context);
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
        if (version is null || !version.IsCurrent || string.IsNullOrWhiteSpace(version.Content) || request is null)
            throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        var context = JsonSerializer.Deserialize<AIStoryInputContextSnapshot>(request.ContextSnapshotJson, JsonOptions)
                      ?? throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        ValidateConsentContext(context);
        return new ArtifactState(job, version, context);
    }

    private static void ValidateConsentContext(AIStoryInputContextSnapshot context)
    {
        if (!context.ConsentRecordedAt.HasValue || context.ConsentPolicyVersion <= 0)
            throw new InvalidOperationException("CONSENT_REQUIRED");
    }

    private async Task<StoryGenerationJob> RequireClaimedJobAsync(
        int jobId, string claimedToken, CancellationToken cancellationToken)
    {
        var job = await _unitOfWork.Repository<StoryGenerationJob>().GetByIdAsync(jobId, cancellationToken);
        if (job is null || job.Status != GenerationJobStatus.Processing || job.ConcurrencyToken != claimedToken)
            throw new InvalidOperationException("STALE_JOB_RESULT");
        return job;
    }

    private async Task<Story> LoadAuthorizedStoryAsync(int userId, int storyId, CancellationToken cancellationToken)
    {
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story", storyId);
        if (story.AuthorUserId == userId) return story;
        var relationship = await _unitOfWork.Repository<SupervisionRelationship>().FirstOrDefaultAsync(
            item => item.ChildProfileId == story.ChildProfileId && item.SupervisorUserId == userId && item.RevokedAt == null,
            cancellationToken: cancellationToken);
        if (relationship is null) throw new ForbiddenException();
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
        RequestId = $"content-job-{job.Id}", ApprovedOutlineReference = $"story-version-{version.Id}",
        Outline = ToOutline(version), AgeBand = context.AgeBand, ReadingLevel = context.ReadingLevel.ToString(),
        VocabularyLevel = context.VocabularyLevel, Language = context.Language,
        ComprehensionGoal = context.ComprehensionGoal,
        StoryParameters = new StoryParametersDto
        {
            Genre = input.Genre, CharacterMode = input.CharacterMode, Characters = input.Characters,
            SettingMode = input.SettingMode, Setting = input.Setting ?? string.Empty, Topic = input.Topic,
            Lesson = input.Lesson, RequestedLength = input.TargetLength
        },
        Constraints = Constraints(context)
    };

    private static GenerationConstraintsDto Constraints(AIStoryInputContextSnapshot context) => new()
    {
        MaximumWords = context.MaximumLength,
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
        Lesson = version.Lesson ?? string.Empty
    };
    private static string Flatten(StoryContentDto story) => string.Join("\n\n", story.StorySections.OrderBy(item => item.Order)
        .Select(item => string.IsNullOrWhiteSpace(item.Heading) ? item.Content.Trim() : $"{item.Heading.Trim()}\n{item.Content.Trim()}"));
    private static IReadOnlyList<string> PolicyTerms(IReadOnlyList<string>? terms, IReadOnlyList<string> fallback) =>
        terms is { Count: > 0 } ? terms : fallback;
    private static bool ContainsTerm(string content, string term) => content.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase);
    private static void RotateToken(StoryGenerationJob job) => job.ConcurrencyToken = Guid.NewGuid().ToString("N");
    private static bool IsContentGenerationOperation(GenerationJobOperation operation) => operation is
        GenerationJobOperation.GenerateContent or GenerationJobOperation.GenerateVocabulary or
        GenerationJobOperation.GenerateQuiz or GenerationJobOperation.GenerateDiscussion;
    private static string OperationName(GenerationJobOperation operation) => operation switch
    {
        GenerationJobOperation.GenerateContent => "content",
        GenerationJobOperation.GenerateVocabulary => "vocabulary",
        GenerationJobOperation.GenerateQuiz => "quiz",
        GenerationJobOperation.GenerateDiscussion => "discussion",
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
        JsonException => "CONTENT_SCHEMA_INVALID",
        _ => operation switch
        {
            GenerationJobOperation.GenerateVocabulary => "VOCABULARY_GENERATION_FAILED",
            GenerationJobOperation.GenerateQuiz => "QUIZ_GENERATION_FAILED",
            GenerationJobOperation.GenerateDiscussion => "DISCUSSION_GENERATION_FAILED",
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
            throw new InvalidOperationException("QUIZ_VALIDATION_FAILED");
        var result = new List<QuizItemSeed>();
        foreach (var item in items)
        {
            var quizText = string.Join(' ', new[] { item.Question, item.CorrectAnswer, item.Explanation }.Concat(item.Options));
            if (string.IsNullOrWhiteSpace(item.Question) || !IsArtifactSafe(quizText, context) ||
                !HasRelevantTerm(item.Question, storyContent))
                throw new InvalidOperationException("QUIZ_VALIDATION_FAILED");
            var type = item.Type.ToLowerInvariant() switch
            {
                "multiple_choice" => QuizType.MultipleChoice,
                "true_false" => QuizType.TrueFalse,
                "short_answer" => QuizType.ShortAnswer,
                _ => throw new InvalidOperationException("QUIZ_VALIDATION_FAILED")
            };
            var answer = item.CorrectAnswer;
            if (type == QuizType.MultipleChoice)
            {
                if (item.Options.Count < 2 || item.CorrectOptionIndex < 0 || item.CorrectOptionIndex >= item.Options.Count)
                    throw new InvalidOperationException("QUIZ_VALIDATION_FAILED");
                answer = string.IsNullOrWhiteSpace(answer) ? item.Options[item.CorrectOptionIndex] : answer;
                if (!item.Options.Contains(answer, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("QUIZ_VALIDATION_FAILED");
            }
            else if (type == QuizType.TrueFalse && !string.Equals(answer, "true", StringComparison.OrdinalIgnoreCase) && !string.Equals(answer, "false", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("QUIZ_VALIDATION_FAILED");
            else if (type == QuizType.ShortAnswer && string.IsNullOrWhiteSpace(answer))
                throw new InvalidOperationException("QUIZ_VALIDATION_FAILED");
            result.Add(new QuizItemSeed(type, item.Question.Trim(), answer.Trim(), JsonSerializer.Serialize(item.Options, JsonOptions)));
        }
        return result;
    }

    private static bool IsArtifactSafe(string value, AIStoryInputContextSnapshot context) =>
        !PolicyTerms(context.BlockedCategoryTerms, context.BlockedCategoryCodes).Any(term => ContainsTerm(value, term)) &&
        !PolicyTerms(context.RestrictedCategoryTerms, context.RestrictedCategoryCodes).Any(term => ContainsTerm(value, term));

    private static bool HasRelevantTerm(string value, string source)
    {
        var terms = value.Split([' ', '\t', '\r', '\n', '?', '!', '.', ',', ':', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Where(item => item.Length >= 4)
            .ToArray();
        return terms.Length == 0 || terms.Any(item => source.Contains(item, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ContentHandoff(StoryGenerationJob Job, StoryGenerationRequest Request, StoryVersion BaseVersion,
        AcceptedAIStoryInputSnapshot Input, AIStoryInputContextSnapshot Context);
    private sealed record CandidateMetadataEnvelope(GenerationMetadataDto Generation, ContentQualityResult Quality);
    private sealed record ArtifactState(StoryGenerationJob Job, StoryVersion Version, AIStoryInputContextSnapshot Context);
    private sealed record QuizItemSeed(QuizType Type, string Question, string CorrectAnswer, string Choices)
    {
        public int StoryVersionId { get; init; }
        public static implicit operator QuizItem(QuizItemSeed item) => new()
        {
            StoryVersionId = item.StoryVersionId, Type = item.Type, Question = item.Question,
            CorrectAnswer = item.CorrectAnswer, Choices = item.Choices
        };
    }
}
