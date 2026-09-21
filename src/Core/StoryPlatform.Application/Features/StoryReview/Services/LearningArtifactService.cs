using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Learning.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Application.Features.StoryReview.Interfaces;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryReview.Services;

/// <summary>
/// Triển khai dịch vụ sinh Learning Artifacts cho Phase 4:
/// - Vocabulary
/// - Quiz
/// - Discussion
/// Hỗ trợ sinh toàn bộ gói hoặc sinh độc lập từng artifact, đảm bảo liên kết chặt chẽ với StoryVersion hiện tại.
/// </summary>
public sealed class LearningArtifactService : ILearningArtifactService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAIStoryGenerationClient _aiClient;
    private readonly ISupervisionAccessGuard _accessGuard;
    private readonly ISafetyPolicyService? _safetyPolicyService;
    private readonly ILearningProfileService? _learningProfileService;
    private readonly IReviewCompletionStore? _reviewCompletionStore;

    public LearningArtifactService(
        IUnitOfWork unitOfWork,
        IAIStoryGenerationClient aiClient,
        ISupervisionAccessGuard accessGuard,
        ISafetyPolicyService? safetyPolicyService = null,
        ILearningProfileService? learningProfileService = null,
        IReviewCompletionStore? reviewCompletionStore = null)
    {
        _unitOfWork = unitOfWork;
        _aiClient = aiClient;
        _accessGuard = accessGuard;
        _safetyPolicyService = safetyPolicyService;
        _learningProfileService = learningProfileService;
        _reviewCompletionStore = reviewCompletionStore;
    }

    public async Task<ArtifactGenerationResultDto> GenerateArtifactsAsync(
        int userId,
        int storyId,
        GenerateArtifactsRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var (story, version) = await ResolveAuthorizedStoryAndVersionAsync(userId, storyId, request?.StoryVersionId, cancellationToken);
        var shouldVocab = request?.IncludeVocabulary ?? true;
        var shouldQuiz = request?.IncludeQuiz ?? true;
        var shouldDiscussion = request?.IncludeDiscussion ?? true;

        var vocabCount = 0;
        var quizCount = 0;
        var discussionCount = 0;

        if (shouldVocab)
        {
            vocabCount = await GenerateVocabularyInternalAsync(userId, story, version, cancellationToken);
        }

        if (shouldQuiz)
        {
            quizCount = await GenerateQuizInternalAsync(userId, story, version, cancellationToken);
        }

        if (shouldDiscussion)
        {
            discussionCount = await GenerateDiscussionInternalAsync(userId, story, version, cancellationToken);
        }

        return new ArtifactGenerationResultDto
        {
            StoryId = story.Id,
            StoryVersionId = version.Id,
            VocabularyCount = vocabCount,
            QuizCount = quizCount,
            DiscussionCount = discussionCount,
            Success = true,
            Message = "Đã sinh thành công các learning artifacts cho Phase 4."
        };
    }

    public async Task<int> GenerateVocabularyAsync(
        int userId, int storyId, int? storyVersionId = null, CancellationToken cancellationToken = default)
    {
        var (story, version) = await ResolveAuthorizedStoryAndVersionAsync(userId, storyId, storyVersionId, cancellationToken);
        return await GenerateVocabularyInternalAsync(userId, story, version, cancellationToken);
    }

    public async Task<int> GenerateQuizAsync(
        int userId, int storyId, int? storyVersionId = null, CancellationToken cancellationToken = default)
    {
        var (story, version) = await ResolveAuthorizedStoryAndVersionAsync(userId, storyId, storyVersionId, cancellationToken);
        return await GenerateQuizInternalAsync(userId, story, version, cancellationToken);
    }

    public async Task<int> GenerateDiscussionAsync(
        int userId, int storyId, int? storyVersionId = null, CancellationToken cancellationToken = default)
    {
        var (story, version) = await ResolveAuthorizedStoryAndVersionAsync(userId, storyId, storyVersionId, cancellationToken);
        return await GenerateDiscussionInternalAsync(userId, story, version, cancellationToken);
    }

    private async Task<(Story Story, StoryVersion Version)> ResolveAuthorizedStoryAndVersionAsync(
        int userId, int storyId, int? storyVersionId, CancellationToken cancellationToken)
    {
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story", storyId);
        await _accessGuard.EnsurePermissionAsync(story.ChildProfileId, userId, Permission.GenerateStory, cancellationToken);

        var versionRepo = _unitOfWork.Repository<StoryVersion>();
        StoryVersion? version;
        if (storyVersionId.HasValue)
        {
            version = await versionRepo.GetByIdAsync(storyVersionId.Value, cancellationToken);
            if (version is null || version.StoryId != storyId)
                throw new BadRequestException("StoryVersion không thuộc Story.");
        }
        else
        {
            version = await versionRepo.FirstOrDefaultAsync(
                v => v.StoryId == storyId && v.IsCurrent, cancellationToken: cancellationToken);
        }

        if (version is null)
            throw new NotFoundException("StoryVersion hiện tại không tồn tại.");
        if (string.IsNullOrWhiteSpace(version.Content))
            throw new BadRequestException("StoryVersion chưa có nội dung để tạo learning artifacts.");

        return (story, version);
    }

    private async Task<int> GenerateVocabularyInternalAsync(
        int userId, Story story, StoryVersion version, CancellationToken cancellationToken)
    {
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(story.ChildProfileId, cancellationToken);
        var learning = await TryGetLearningProfileAsync(story.ChildProfileId, userId, cancellationToken);
        var ageBand = child?.AgeBand.ToString() ?? "Age_6_8";

        var request = new GenerateVocabularyRequest
        {
            RequestId = $"p4-vocab-{story.Id}-{version.Id}-{Guid.NewGuid():N}",
            Story = new StoryContentDto
            {
                Title = version.Title,
                Lesson = version.Lesson ?? string.Empty,
                StorySections = [new StorySectionDto(1, string.Empty, version.Content ?? string.Empty)]
            },
            AgeBand = ageBand,
            ReadingLevel = (learning?.ReadingLevel ?? story.ReadingLevel ?? 2).ToString(),
            VocabularyLevel = (learning?.ReadingLevel ?? story.ReadingLevel ?? 2).ToString(),
            Language = story.Language
        };

        var response = await _aiClient.GenerateVocabularyAsync(request, cancellationToken);
        var items = response.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.Term) && !string.IsNullOrWhiteSpace(i.Definition))
            .DistinctBy(i => i.Term.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (items.Count == 0)
        {
            throw new InvalidOperationException("Không thể trích xuất từ vựng phù hợp từ câu chuyện.");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(story.Id, cancellationToken);
            var vocabRepo = _unitOfWork.Repository<StoryVocabulary>();
            var existing = await vocabRepo.FindAsync(v => v.StoryVersionId == version.Id, cancellationToken: cancellationToken);
            foreach (var e in existing)
            {
                vocabRepo.Delete(e);
            }

            foreach (var item in items)
            {
                await vocabRepo.AddAsync(new StoryVocabulary
                {
                    StoryVersionId = version.Id,
                    Term = item.Term.Trim(),
                    Definition = item.Definition.Trim()
                }, cancellationToken);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return items.Count;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private async Task<int> GenerateQuizInternalAsync(
        int userId, Story story, StoryVersion version, CancellationToken cancellationToken)
    {
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(story.ChildProfileId, cancellationToken);
        var learning = await TryGetLearningProfileAsync(story.ChildProfileId, userId, cancellationToken);
        var safety = await TryGetSafetyPolicyAsync(story.ChildProfileId, userId, cancellationToken);
        var ageBand = child?.AgeBand.ToString() ?? "Age_6_8";

        var vocabRepo = _unitOfWork.Repository<StoryVocabulary>();
        var currentVocab = await vocabRepo.FindAsync(v => v.StoryVersionId == version.Id, cancellationToken: cancellationToken);
        var vocabDtos = currentVocab.Select(v => new GeneratedVocabularyItemDto(v.Term, v.Definition)).ToList();

        var request = new GenerateQuizRequest
        {
            RequestId = $"p4-quiz-{story.Id}-{version.Id}-{Guid.NewGuid():N}",
            Story = new StoryContentDto
            {
                Title = version.Title,
                Lesson = version.Lesson ?? string.Empty,
                StorySections = [new StorySectionDto(1, string.Empty, version.Content ?? string.Empty)]
            },
            Vocabulary = vocabDtos,
            AgeBand = ageBand,
            Language = story.Language,
            ComprehensionGoal = learning?.ComprehensionGoal,
            ComprehensionThresholdPercent = safety?.ComprehensionThresholdPercent
        };

        var response = await _aiClient.GenerateQuizAsync(request, cancellationToken);
        var items = response.Items.ToList();
        if (items.Count == 0)
        {
            throw new InvalidOperationException("Không thể tạo câu hỏi trắc nghiệm từ câu chuyện.");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(story.Id, cancellationToken);
            var quizRepo = _unitOfWork.Repository<QuizItem>();
            var existing = await quizRepo.FindAsync(q => q.StoryVersionId == version.Id, cancellationToken: cancellationToken);
            foreach (var e in existing)
            {
                quizRepo.Delete(e);
            }

            foreach (var item in items)
            {
                var type = item.Type?.ToLowerInvariant() switch
                {
                    "multiple_choice" or "multiplechoice" => QuizType.MultipleChoice,
                    "true_false" or "truefalse" => QuizType.TrueFalse,
                    "short_answer" or "shortanswer" => QuizType.ShortAnswer,
                    _ => QuizType.MultipleChoice
                };
                var choicesJson = item.Options != null ? JsonSerializer.Serialize(item.Options) : null;

                await quizRepo.AddAsync(new QuizItem
                {
                    StoryVersionId = version.Id,
                    Type = type,
                    Question = item.Question.Trim(),
                    CorrectAnswer = item.CorrectAnswer.Trim(),
                    Choices = choicesJson
                }, cancellationToken);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return items.Count;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private async Task<int> GenerateDiscussionInternalAsync(
        int userId, Story story, StoryVersion version, CancellationToken cancellationToken)
    {
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(story.ChildProfileId, cancellationToken);
        var learning = await TryGetLearningProfileAsync(story.ChildProfileId, userId, cancellationToken);
        var ageBand = child?.AgeBand.ToString() ?? "Age_6_8";

        var request = new GenerateDiscussionRequest
        {
            RequestId = $"p4-discussion-{story.Id}-{version.Id}-{Guid.NewGuid():N}",
            Story = new StoryContentDto
            {
                Title = version.Title,
                Lesson = version.Lesson ?? string.Empty,
                StorySections = [new StorySectionDto(1, string.Empty, version.Content ?? string.Empty)]
            },
            AgeBand = ageBand,
            Language = story.Language,
            ComprehensionGoal = learning?.ComprehensionGoal
        };

        var response = await _aiClient.GenerateDiscussionAsync(request, cancellationToken);
        var questions = response.Items.Select(q => q.Question.Trim()).Where(q => q.Length > 0).Distinct().ToList();
        if (questions.Count == 0)
        {
            throw new InvalidOperationException("Không thể tạo câu hỏi thảo luận từ câu chuyện.");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(story.Id, cancellationToken);
            var discRepo = _unitOfWork.Repository<DiscussionQuestion>();
            var existing = await discRepo.FindAsync(d => d.StoryVersionId == version.Id, cancellationToken: cancellationToken);
            foreach (var e in existing)
            {
                discRepo.Delete(e);
            }

            for (var i = 0; i < questions.Count; i++)
            {
                await discRepo.AddAsync(new DiscussionQuestion
                {
                    StoryVersionId = version.Id,
                    Question = questions[i],
                    IsMoralLesson = i == 0 // câu đầu tiên tập trung bài học đạo đức
                }, cancellationToken);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return questions.Count;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private async Task<LearningProfile?> TryGetLearningProfileAsync(int childProfileId, int userId, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<LearningProfile>().FirstOrDefaultAsync(
            lp => lp.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
    }

    private async Task<SafetyPolicy?> TryGetSafetyPolicyAsync(int childProfileId, int userId, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            sp => sp.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
    }
}
