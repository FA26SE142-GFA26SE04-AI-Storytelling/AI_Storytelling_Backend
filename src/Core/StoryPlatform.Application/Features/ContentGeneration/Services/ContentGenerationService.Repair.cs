using System.Text.Json;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.ContentGeneration.Quality;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentGeneration.Services;

public sealed partial class ContentGenerationService
{
    private const string RepairPrefix = "repair:";

    // Repair a legacy stable version without modifying it or reusing its dependent learning artifacts.
    private Task<bool> TryQueueReadabilityRepairAsync(int userId, int storyId, string key, CancellationToken ct) =>
        InStoryTransactionAsync(storyId, async () =>
        {
            var story = await LoadAuthorizedStoryAsync(userId, storyId, ct);
            var prior = await _unitOfWork.Repository<StoryAuthoringOperation>().FirstOrDefaultAsync(
                x => x.StoryId == storyId && x.OperationId == key, cancellationToken: ct);
            if (prior?.Kind == "content-repair")
            {
                if (prior.ActorUserId != userId) throw new ConflictException("RETRY_KEY_CONFLICT");
                return true;
            }
            var version = await _unitOfWork.Repository<StoryVersion>().FirstOrDefaultAsync(
                x => x.StoryId == storyId && x.IsCurrent && x.Content != null, cancellationToken: ct);
            if (version is null || story.Source != StorySource.Ai || story.Status is not (StoryStatus.ContentReview or StoryStatus.OutlineReview)) return false;
            await CreatorPublicationGuard.EnsureWritableAsync(_unitOfWork, storyId, version.Id, ct, contentMutation: true);
            var requests = await _unitOfWork.Repository<StoryGenerationRequest>().FindAsync(
                x => x.StoryId == storyId && x.Status == GenerationInputStatus.InputAccepted, cancellationToken: ct);
            var request = requests.OrderByDescending(x => x.Id).FirstOrDefault();
            if (request is null) return false;
            var context = JsonSerializer.Deserialize<AIStoryInputContextSnapshot>(request.ContextSnapshotJson, JsonOptions)
                ?? throw new ConflictException("CONTEXT_SNAPSHOT_MISSING");
            GenerationStoryAccess.ValidateSnapshot(story, context);
            if (ReadabilityCalculator.EvaluateForProfile(version.Content, context.Language, context.ReadingLevel, context.ReadabilityScoreThreshold).Passed) return false;
            var policy = story.Scope == StoryScope.Parent ? await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
                x => x.ChildProfileId == story.ChildProfileId && !x.IsDeleted, cancellationToken: ct) : null;
            if (story.Scope == StoryScope.Parent && (policy is null || !policy.ConsentRecorded || !policy.ConsentRecordedAt.HasValue ||
                ConsentPolicy.IsOutdated(policy.ConsentPolicyVersion))) throw new ForbiddenException("CONSENT_REQUIRED");
            var level = story.ReadingLevel ?? context.ReadingLevel;
            if (level != context.ReadingLevel || policy is not null &&
                ContentReadabilityLimits.ForLevel(level, policy.ReadabilityScoreThreshold) !=
                ContentReadabilityLimits.ForLevel(level, context.ReadabilityScoreThreshold))
                throw new ConflictException("READABILITY_POLICY_CHANGED");
            if (prior is not null) throw new ConflictException("RETRY_KEY_CONFLICT");
            if (await _unitOfWork.Repository<StoryGenerationJob>().ExistsAsync(
                x => x.StoryId == storyId && (x.Status == GenerationJobStatus.Pending || x.Status == GenerationJobStatus.Processing), ct))
                throw new ConflictException("GENERATION_JOB_ACTIVE");
            if (!version.OutlineApprovedAt.HasValue) throw new ConflictException("APPROVED_OUTLINE_REQUIRED");
            await _unitOfWork.Repository<StoryGenerationJob>().AddAsync(new()
            {
                StoryId = storyId, GenerationRequestId = request.Id, BaseStoryVersionId = version.Id,
                RequestedByUserId = userId, Operation = GenerationJobOperation.GenerateContent,
                OperationKey = RepairPrefix + ReadabilityEvaluationEvidence.Hash(key), Stage = JobStage.ContentPending, Status = GenerationJobStatus.Pending,
                StartedAt = DateTime.UtcNow, MaxAttempts = 3
            }, ct);
            await _unitOfWork.Repository<StoryAuthoringOperation>().AddAsync(new()
            {
                StoryId = storyId, StoryVersionId = version.Id, ActorUserId = userId, OperationId = key,
                Kind = "content-repair", RequestHash = ReadabilityEvaluationEvidence.Hash(version.Content!),
                OperationResponseJson = JsonSerializer.Serialize(new { sourceVersionId = version.Id }, JsonOptions)
            }, ct);
            story.Status = StoryStatus.OutlineReview;
            _unitOfWork.Repository<Story>().Update(story);
            return true;
        }, ct);

    private Task<RefineStoryContentResponse> RefineContentAsync(ContentHandoff handoff, StoryContentDto content,
        ContentQualityResult quality, int refinement, int budget, CancellationToken ct) =>
        _aiClient.RefineStoryContentAsync(new RefineStoryContentRequest
        {
            ExecutionBudgetSeconds = budget,
            RequestId = $"content-job-{handoff.Job.Id}-refine-{refinement}",
            Story = content, Outline = ToOutline(handoff.BaseVersion), AgeBand = handoff.Context.AgeBand,
            ReadingLevel = handoff.Context.ReadingLevel.ToString(), VocabularyLevel = handoff.Context.VocabularyLevel,
            Language = handoff.Context.Language, TargetLength = handoff.Input.TargetLength,
            Constraints = Constraints(handoff.Context), Reasons = quality.RefinementReasons,
            Feedback = new()
            {
                Readability = quality.ReadabilityDetails,
                CurrentWordCount = StoryWordCounter.Count(string.Join(' ', content.StorySections.Select(x => x.Content))),
                TargetWords = handoff.Input.TargetLength,
                MinimumWords = handoff.Context.LearningProfile?.MinimumWords ?? Math.Max(100, (int)Math.Floor(handoff.Input.TargetLength * .7)),
                MaximumWords = Constraints(handoff.Context).MaximumWords,
                FailedGateCodes = new[] { quality.Length, quality.Readability, quality.Safety, quality.OutlineConsistency, quality.VocabularyCompliance }
                    .Where(x => !x.Passed && x.CanRefine && x.ReasonCode is not null).Select(x => x.ReasonCode!).ToArray()
            }
        }, ct);
}
