using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.ChildProfiles.Learning.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Learning.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;
using StoryPlatform.Application.Features.ExistingStories.Helpers;
using StoryPlatform.Application.Features.ExistingStories.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ExistingStories.Services;

/// <summary>
/// Điểm hội tụ duy nhất để đưa một StoryVersion đã ổn định vào chuỗi artifact
/// Vocabulary → Quiz → Discussion (không qua GenerateContent).
///
/// Hợp đồng:
///   - storyVersion.IsCurrent == true
///   - !string.IsNullOrWhiteSpace(version.Content)
///   - safety đã pass (caller đảm bảo)
///   - story.Source ∈ { Ai, Manual }
///   - story.Status ∈ { OutlineReview (chỉ AI), Draft (Existing) }
///
/// Kết quả:
///   - Đảm bảo có 1 StoryGenerationRequest với ContextSnapshotJson hợp lệ
///     (tái sử dụng nếu có, tạo "ảo" cho Existing Story).
///   - Enqueue job GenerateVocabulary đầu tiên (idempotent theo (story, version)).
/// </summary>
public sealed class StableVersionArtifactHandoffService : IStableVersionArtifactHandoffService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILearningProfileService _learningProfileService;
    private readonly ISafetyPolicyService _safetyPolicyService;

    public StableVersionArtifactHandoffService(
        IUnitOfWork unitOfWork,
        ILearningProfileService learningProfileService,
        ISafetyPolicyService safetyPolicyService)
    {
        _unitOfWork = unitOfWork;
        _learningProfileService = learningProfileService;
        _safetyPolicyService = safetyPolicyService;
    }

    public async Task<int> QueueArtifactsAsync(
        int storyId,
        int storyVersionId,
        int requestedByUserId,
        int? generationRequestId,
        CancellationToken cancellationToken = default)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, cancellationToken);

            var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken)
                        ?? throw new NotFoundException("Story", storyId);
            var version = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(storyVersionId, cancellationToken)
                          ?? throw new NotFoundException("StoryVersion", storyVersionId);

            ValidateHandoffContract(story, version);

            // Đảm bảo có GenerationRequest (ảo nếu Existing Story, có sẵn nếu AI Story).
            var requestId = await EnsureGenerationRequestAsync(
                story, version, requestedByUserId, generationRequestId, cancellationToken);

            // Idempotency: nếu đã có job GenerateVocabulary cho (story, version) thì trả về job đó.
            var existingJob = await _unitOfWork.Repository<StoryGenerationJob>().FirstOrDefaultAsync(
                job => job.StoryId == storyId
                       && job.Operation == GenerationJobOperation.GenerateVocabulary
                       && job.StoryVersionId == storyVersionId,
                cancellationToken: cancellationToken);
            if (existingJob is not null)
            {
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                return existingJob.Id;
            }

            // Tạo job đầu chain.
            var jobEntity = new StoryGenerationJob
            {
                StoryId = storyId,
                GenerationRequestId = requestId,
                StoryVersionId = storyVersionId,
                BaseStoryVersionId = storyVersionId,
                RequestedByUserId = requestedByUserId,
                OperationKey = ExistingStoryIdempotencyKeys.ForArtifactHandoff(storyId, storyVersionId, 4),
                Operation = GenerationJobOperation.GenerateVocabulary,
                Stage = JobStage.ContentArtifactPending,
                Status = GenerationJobStatus.Pending,
                AttemptNo = 0,
                MaxAttempts = 3,
                StartedAt = DateTime.UtcNow
            };
            await _unitOfWork.Repository<StoryGenerationJob>().AddAsync(jobEntity, cancellationToken);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return jobEntity.Id;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private static void ValidateHandoffContract(Story story, StoryVersion version)
    {
        if (version.StoryId != story.Id)
            throw new BadRequestException("StoryVersion không thuộc Story.");
        if (!version.IsCurrent)
            throw new BadRequestException("StoryVersion không phải current.");
        if (string.IsNullOrWhiteSpace(version.Content))
            throw new BadRequestException("StoryVersion chưa có nội dung.");

        // Source + Status guard
        if (story.Source == StorySource.Manual)
        {
            // Existing Story: bỏ qua Outline. Status phải ở Draft hoặc OutlineReview (re-entry).
            if (story.Status is not (StoryStatus.Draft or StoryStatus.OutlineReview))
                throw new ConflictException($"STORY_STATUS_INVALID:{story.Status}");
        }
        else if (story.Source == StorySource.Ai)
        {
            // AI Story: caller phải đảm bảo OutlineApprovedAt đã có.
            if (!version.OutlineApprovedAt.HasValue)
                throw new BadRequestException("StoryVersion AI Story thiếu OutlineApprovedAt.");
            if (story.Status is not (StoryStatus.OutlineReview or StoryStatus.Draft))
                throw new ConflictException($"STORY_STATUS_INVALID:{story.Status}");
        }
        else
        {
            throw new BadRequestException($"Story.Source không được hỗ trợ: {story.Source}");
        }
    }

    private async Task<int> EnsureGenerationRequestAsync(
        Story story,
        StoryVersion version,
        int requestedByUserId,
        int? providedRequestId,
        CancellationToken cancellationToken)
    {
        // 1. Chỉ tái sử dụng request đã vượt qua input guardrail.
        if (providedRequestId.HasValue)
        {
            var existing = await _unitOfWork.Repository<StoryGenerationRequest>()
                .GetByIdAsync(providedRequestId.Value, cancellationToken);
            if (existing is null || existing.StoryId != story.Id)
                throw new NotFoundException("StoryGenerationRequest", providedRequestId.Value);
            if (existing.Status != GenerationInputStatus.InputAccepted)
                throw new ConflictException($"GENERATION_REQUEST_NOT_ACCEPTED:{existing.Status}");
            return existing.Id;
        }

        // 2. Nếu Story đã có request accepted, dùng lại và đảm bảo ContextSnapshotJson đầy đủ consent.
        var activeRequest = (await _unitOfWork.Repository<StoryGenerationRequest>()
                .FindAsync(req => req.StoryId == story.Id
                                  && req.Status == GenerationInputStatus.InputAccepted,
                    cancellationToken: cancellationToken))
            .OrderByDescending(req => req.Id)
            .FirstOrDefault();
        if (activeRequest is not null)
        {
            var (_, freshContext) = await BuildExistingStorySnapshotsAsync(story, version, cancellationToken);
            activeRequest.ContextSnapshotJson = JsonSerializer.Serialize(freshContext, JsonOptions);
            _unitOfWork.Repository<StoryGenerationRequest>().Update(activeRequest);
            return activeRequest.Id;
        }

        // 3. Tạo request "ảo" cho Existing Story.
        var (input, context) = await BuildExistingStorySnapshotsAsync(story, version, cancellationToken);

        var request = new StoryGenerationRequest
        {
            StoryId = story.Id,
            SubmittedByUserId = requestedByUserId,
            IdempotencyKey = ExistingStoryIdempotencyKeys.ForArtifactHandoff(story.Id, version.Id, 0),
            InputFingerprint = ShortHash(JsonSerializer.Serialize(input, JsonOptions)),
            ContextFingerprint = ShortHash(JsonSerializer.Serialize(context, JsonOptions)),
            ContextSnapshotJson = JsonSerializer.Serialize(context, JsonOptions),
            AcceptedInputJson = JsonSerializer.Serialize(input, JsonOptions),
            Status = GenerationInputStatus.InputAccepted,
            AttemptCount = 0,
            MaxAttempts = 1,
            GuardrailDecision = "Allow",
            GuardrailCheckVersion = "existing-story-import/v1",
            GuardrailCheckedAt = DateTime.UtcNow,
            CanRetry = false
        };
        await _unitOfWork.Repository<StoryGenerationRequest>().AddAsync(request, cancellationToken);
        return request.Id;
    }

    private async Task<(AcceptedAIStoryInputSnapshot input, AIStoryInputContextSnapshot context)>
        BuildExistingStorySnapshotsAsync(Story story, StoryVersion version, CancellationToken cancellationToken)
    {
        var input = new AcceptedAIStoryInputSnapshot(
            Topic: string.IsNullOrWhiteSpace(version.Title) ? "ExistingStory" : version.Title.Trim(),
            Genre: string.IsNullOrWhiteSpace(story.Genre) ? null : story.Genre,
            CharacterMode: "ai_suggested",
            Characters: Array.Empty<string>(),
            SettingMode: "ai_suggested",
            Setting: null,
            Lesson: version.Lesson ?? string.Empty,
            VocabularyLevel: story.VocabularyLevel ?? "level_2",
            Language: story.Language ?? "vi",
            TargetLength: StoryContentNormalizer.CountWords(version.Content ?? string.Empty));

        var context = await BuildContextSnapshotAsync(story, cancellationToken);
        return (input, context);
    }

    private async Task<AIStoryInputContextSnapshot> BuildContextSnapshotAsync(
        Story story, CancellationToken cancellationToken)
    {
        var childProfileId = story.ChildProfileId;

        LearningProfileDto? learning = null;
        try
        {
            learning = await _learningProfileService.GetLearningProfileAsync(
                childProfileId, story.AuthorUserId, cancellationToken);
        }
        catch (NotFoundException)
        {
            learning = null;
        }

        SafetyPolicyDto? safety = null;
        try
        {
            safety = await _safetyPolicyService.GetSafetyPolicyAsync(
                childProfileId, story.AuthorUserId, cancellationToken);
        }
        catch (NotFoundException)
        {
            safety = null;
        }

        return new AIStoryInputContextSnapshot(
            ChildProfileId: childProfileId,
            AgeBand: story.AgeBand,
            ReadingLevel: learning?.ReadingLevel ?? 2,
            VocabularyLevel: learning is not null ? "level_" + learning.ReadingLevel : (story.VocabularyLevel ?? "level_2"),
            Language: story.Language ?? "vi",
            MaximumLength: safety?.MaxStoryLength ?? 5000,
            RequiredApprovalMode: safety?.RequiredApprovalMode ?? ApprovalMode.AlwaysManual.ToString(),
            Interests: learning?.Topics?.Where(t => string.Equals(t.Relation, "Favorite", StringComparison.OrdinalIgnoreCase))
                              .Select(t => t.Topic).ToArray() ?? Array.Empty<string>(),
            AllowedCategoryCodes: safety?.Categories?
                .Where(c => string.Equals(c.Rule, PolicyRule.Allowed.ToString(), StringComparison.OrdinalIgnoreCase))
                .Select(c => c.ContentCategoryId.ToString()).ToArray() ?? Array.Empty<string>(),
            RestrictedCategoryCodes: safety?.Categories?
                .Where(c => string.Equals(c.Rule, PolicyRule.Restricted.ToString(), StringComparison.OrdinalIgnoreCase))
                .Select(c => c.ContentCategoryId.ToString()).ToArray() ?? Array.Empty<string>(),
            BlockedCategoryCodes: safety?.Categories?
                .Where(c => string.Equals(c.Rule, PolicyRule.Blocked.ToString(), StringComparison.OrdinalIgnoreCase))
                .Select(c => c.ContentCategoryId.ToString()).ToArray() ?? Array.Empty<string>(),
            ReadabilityScoreThreshold: safety?.ReadabilityScoreThreshold,
            ParentalGateEnabled: safety?.ParentalGateEnabled ?? true,
            SafetyScoreThreshold: safety?.SafetyScoreThreshold,
            ComprehensionGoal: learning?.ComprehensionGoal,
            ComprehensionThresholdPercent: safety?.ComprehensionThresholdPercent,
            ComprehensionWindowSize: safety?.ComprehensionWindowSize ?? 3,
            ConsentPolicyVersion: safety?.ConsentPolicyVersion ?? 1,
            ConsentRecordedAt: safety?.ConsentRecordedAt);
    }

    private static string ShortHash(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }
}
