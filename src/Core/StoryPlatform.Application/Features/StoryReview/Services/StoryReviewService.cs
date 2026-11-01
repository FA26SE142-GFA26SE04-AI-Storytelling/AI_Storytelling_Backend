using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.Moderation;
using System.Text.Json;
using System.Linq;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentGeneration.Quality;
using StoryPlatform.Application.Features.ExistingStories.DTOs;
using StoryPlatform.Application.Features.ExistingStories.Interfaces;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Application.Features.StoryReview.Interfaces;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryReview.Services;

public sealed partial class StoryReviewService : IStoryReviewService
{
    private static readonly JsonSerializerOptions ProposalJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAIStoryGenerationClient _aiClient;
    private readonly IProposalCache _proposalCache;
    private readonly GrayZoneContentCheckService? _moderation;
    private readonly IReviewCompletionStore? _reviewCompletionStore;
    private readonly IExistingStoryEvaluationCache? _evaluationCache;
    private readonly IGenericRepository<Story> _storyRepo;
    private readonly IGenericRepository<StoryVersion> _versionRepo;
    private readonly IGenericRepository<StoryVocabulary> _vocabRepo;
    private readonly IGenericRepository<QuizItem> _quizRepo;

    public StoryReviewService(
        IUnitOfWork unitOfWork,
        IAIStoryGenerationClient aiClient,
        IProposalCache proposalCache,
        IReviewCompletionStore? reviewCompletionStore = null,
        IExistingStoryEvaluationCache? evaluationCache = null, GrayZoneContentCheckService? moderation = null)
    {
        _unitOfWork = unitOfWork;
        _aiClient = aiClient;
        _proposalCache = proposalCache;
        _reviewCompletionStore = reviewCompletionStore;
        _evaluationCache = evaluationCache; _moderation = moderation;
        _storyRepo = unitOfWork.Repository<Story>();
        _versionRepo = unitOfWork.Repository<StoryVersion>();
        _vocabRepo = unitOfWork.Repository<StoryVocabulary>();
        _quizRepo = unitOfWork.Repository<QuizItem>();
    }

    #region Package

    public async Task<ReviewPackageDto> GetReviewPackageAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        await AuthorizeCreatorOperationAsync(userId, storyId, cancellationToken);
        var story = await _storyRepo.FirstOrDefaultAsync(s => s.Id == storyId, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("Story");
        if (StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story)) throw new ConflictException("PARENT_MANUAL_USE_AUTHORING_API");
        GenerationStoryAccess.EnsureSupported(story);

        var version = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("StoryVersion");

        var vocabCount = await _vocabRepo.CountAsync(v => v.StoryVersionId == version.Id, cancellationToken);
        var quizCount = await _quizRepo.CountAsync(q => q.StoryVersionId == version.Id, cancellationToken);

        var readability = ReadabilityCalculator.Calculate(version.Content, story.Language);
        var readingMediaOnly = story.OutputMode == StoryOutputMode.ReadingMediaOnly;

        return new ReviewPackageDto
        {
            AssistanceRevisionToken = story.Source == StorySource.Ai && story.Status == StoryStatus.ContentReview && story.AuthorUserId == userId
                ? story.AuthoringRevisionToken : null,
            StoryId = storyId,
            OutputMode = StoryOutputModeContract.Format(story.OutputMode),
            StoryVersionId = version.Id,
            StoryStatus = story.Status.ToString(),
            Title = version.Title,
            Content = version.Content ?? string.Empty,
            Lesson = version.Lesson ?? string.Empty,
            ReadabilityAlgorithm = readability.Algorithm,
            ReadabilityFkgl = version.ReadabilityFkgl ?? readability.Fkgl,
            ReadabilityFre = version.ReadabilityFre ?? readability.Fre,
            Vocabulary = new ArtifactStatusDto { State = readingMediaOnly ? "skipped" : vocabCount > 0 ? "completed" : "pending", ItemCount = vocabCount },
            Quiz = new ArtifactStatusDto { State = readingMediaOnly ? "skipped" : quizCount > 0 ? "completed" : "pending", ItemCount = quizCount },
            CanEdit = story.Scope != StoryScope.Creator || story.Status == StoryStatus.ContentReview,
            CanApprove = story.Scope != StoryScope.Creator && story.Status == StoryStatus.ContentReview,
            CanConfirmContent = story.Scope == StoryScope.Creator && story.Status == StoryStatus.ContentReview,
            CanArchive = story.Status == StoryStatus.ContentReview
        };
    }

    #endregion

    #region Story Review

    public async Task<StoryReviewDto> GetStoryForReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        await AuthorizeCreatorOperationAsync(userId, storyId, cancellationToken);
        var story = await _storyRepo.GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story");
        if (StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story)) throw new ConflictException("PARENT_MANUAL_USE_AUTHORING_API");
        GenerationStoryAccess.EnsureSupported(story);
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("StoryVersion");
        var readability = ReadabilityCalculator.Calculate(version.Content, story.Language);

        return new StoryReviewDto
        {
            StoryId = storyId,
            OutputMode = StoryOutputModeContract.Format(story.OutputMode),
            VersionId = version.Id,
            Title = version.Title,
            Content = version.Content ?? string.Empty,
            Lesson = version.Lesson ?? string.Empty,
            ReadabilityAlgorithm = readability.Algorithm,
            ReadabilityFkgl = version.ReadabilityFkgl ?? readability.Fkgl,
            ReadabilityFre = version.ReadabilityFre ?? readability.Fre
        };
    }

    public async Task<StoryReviewDto> UpdateStoryAsync(int userId, int storyId, UpdateStoryRequestDto input, CancellationToken cancellationToken = default)
    {
        await AuthorizeCreatorOperationAsync(userId, storyId, cancellationToken);
        if (string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Content) || string.IsNullOrWhiteSpace(input.Lesson))
        {
            throw new BadRequestException("Title, content and lesson are required.");
        }

        // Phase 4 hardening: KHÔNG ghi đè current version.
        // Mọi chỉnh sửa phải tạo StoryVersion mới (EditType = HumanEdited) để bảo toàn audit trail.
        var story = await _storyRepo.GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story");
        if (StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story)) throw new ConflictException("PARENT_MANUAL_USE_AUTHORING_API");
        GenerationStoryAccess.EnsureSupported(story);
        await EnsureReviewPermissionAsync(story, userId, cancellationToken);

        decimal? creatorSafetyScore = null;
        if (story.Scope == StoryScope.Creator)
        {
            await RequireEditableCreatorVersionAsync(story, userId, input.VersionId, cancellationToken);
            var context = await LoadCreatorContextAsync(story, cancellationToken);
            var safety = await _aiClient.EvaluateContentSafetyAsync(new EvaluateContentSafetyRequest
            {
                RequestId = $"creator-edit-{storyId}-{input.VersionId}-{Guid.NewGuid():N}",
                AgeBand = context.AgeBand,
                Language = context.Language,
                BlockedTopics = context.BlockedCategoryTerms ?? context.BlockedCategoryCodes,
                RestrictedTopics = context.RestrictedCategoryTerms ?? context.RestrictedCategoryCodes,
                Story = new StoryContentDto
                {
                    Title = input.Title,
                    Lesson = input.Lesson,
                    StorySections = [new StorySectionDto(1, string.Empty, input.Content)]
                }
            }, cancellationToken);
            creatorSafetyScore = (decimal)(safety.SafetyScore ?? (safety.IsAllowed ? 100d : 0d));
            if (!safety.IsAllowed || creatorSafetyScore < context.SafetyScoreThreshold)
                throw new BadRequestException("CONTENT_SAFETY_SCORE_NOT_MET");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, cancellationToken);
            // GetById uses EF Find: discard its tracked pre-provider copy before reading fresh rows.
            story = await _storyRepo.FirstOrDefaultAsync(x => x.Id == storyId, cancellationToken: cancellationToken)
                ?? throw new NotFoundException("Story");
            await EnsureReviewPermissionAsync(story, userId, cancellationToken);
            var currentVersion = story.Scope == StoryScope.Creator
                ? await RequireEditableCreatorVersionAsync(story, userId, input.VersionId, cancellationToken)
                : await _versionRepo.FirstOrDefaultAsync(
                    v => v.Id == input.VersionId && v.StoryId == storyId && v.IsCurrent,
                    cancellationToken: cancellationToken) ?? throw new NotFoundException("StoryVersion");

            // Tạo version mới với EditType = HumanEdited, version_no = current + 1.
            var assistanceProvenance = await ValidateReviewAssistanceAsync(userId, story, currentVersion.Id, input.AssistanceReferences, cancellationToken);
            var versions = await _versionRepo.FindAsync(
                v => v.StoryId == storyId, cancellationToken: cancellationToken);
            var nextVersionNo = versions.Select(v => v.VersionNo).DefaultIfEmpty().Max() + 1;

            currentVersion.IsCurrent = false;
            _versionRepo.Update(currentVersion);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var readability = ReadabilityCalculator.Calculate(input.Content, story.Language);

            var newVersion = new StoryVersion
            {
                StoryId = storyId,
                VersionNo = nextVersionNo,
                EditType = VersionEditType.HumanEdited,
                EditorUserId = userId,
                Title = input.Title.Trim(),
                Content = input.Content.Trim(),
                Lesson = input.Lesson.Trim(),
                OutlineOpening = currentVersion.OutlineOpening,
                OutlineDevelopment = currentVersion.OutlineDevelopment,
                OutlineEnding = currentVersion.OutlineEnding,
                ReadabilityFkgl = readability.Fkgl,
                ReadabilityFre = readability.Fre,
                SafetyScore = creatorSafetyScore,
                IsCurrent = true,
                OutlineApprovedAt = currentVersion.OutlineApprovedAt,
                OutlineApprovedByUserId = currentVersion.OutlineApprovedByUserId
            };
            newVersion.AuthoringMetadata.AssistanceProvenanceJson = assistanceProvenance;
            await _versionRepo.AddAsync(newVersion, cancellationToken);
            story.AuthoringRevisionToken = Guid.NewGuid().ToString("N");

            // Đồng bộ Story header cho khớp với current version.
            story.Title = newVersion.Title;
            story.Content = newVersion.Content;
            story.MoralLesson = newVersion.Lesson;
            _storyRepo.Update(story);

            // Artifact revalidation: vocab/quiz/tương tác lúc nghe đã sinh cho version cũ không
            // còn phù hợp -> đánh dấu job cũ là stale. Phase 3 worker sẽ tự skip job cũ
            // (qua StoryVersionId != current), và controller Phase 4 sẽ yêu cầu revalidate.
            // Ở đây ta KHÔNG xoá artifact cũ để giữ audit trail; chỉ đảm bảo chúng
            // không được dùng lại cho version mới (worker đã check version.IsCurrent).

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return new StoryReviewDto
            {
                StoryId = storyId,
                OutputMode = StoryOutputModeContract.Format(story.OutputMode),
                VersionId = newVersion.Id,
                Title = newVersion.Title,
                Content = newVersion.Content,
                Lesson = newVersion.Lesson,
                ReadabilityAlgorithm = readability.Algorithm,
                ReadabilityFkgl = newVersion.ReadabilityFkgl,
                ReadabilityFre = newVersion.ReadabilityFre
            };
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<CreateProposalResponseDto> CreateProposalAsync(int userId, int storyId, PartialEditRequestDto input, CancellationToken cancellationToken = default)
    {
        await AuthorizeCreatorOperationAsync(userId, storyId, cancellationToken);
        var sourceStory = await _storyRepo.GetByIdAsync(storyId, cancellationToken) ?? throw new NotFoundException("Story");
        if (StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(sourceStory)) throw new ConflictException("PARENT_MANUAL_USE_AUTHORING_API");
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.Id == input.VersionId && v.StoryId == storyId, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("StoryVersion");

        var currentContent = version.Content ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input.Instruction) ||
            input.Selection.Start < 0 ||
            input.Selection.EndExclusive <= input.Selection.Start ||
            input.Selection.EndExclusive > currentContent.Length)
        {
            throw new BadRequestException("A valid text selection and edit instruction are required.");
        }

        var selectedText = currentContent[input.Selection.Start..input.Selection.EndExclusive];
        if (!string.IsNullOrEmpty(input.Selection.Text) &&
            !string.Equals(input.Selection.Text, selectedText, StringComparison.Ordinal))
        {
            throw new BadRequestException("Selected text does not match the current story content.");
        }

        // Call AI to refine the selected portion
        var story = new StoryContentDto
        {
            Title = version.Title,
            Lesson = version.Lesson ?? string.Empty,
            StorySections = new List<StorySectionDto>
            {
                new(1, string.Empty, selectedText)
            }
        };

        var audience = await _storyRepo.GetByIdAsync(storyId, cancellationToken) ?? throw new NotFoundException("Story", storyId);
        var request = new RefineStoryContentRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Story = story,
            Language = audience.Language,
            AgeBand = audience.AgeBand,
            Reasons = new List<string> { input.Instruction }
        };

        var result = await _aiClient.RefineStoryContentAsync(request, cancellationToken);

        var proposal = new AIProposalDto
        {
            RequestedByUserId = userId,
            StoryId = storyId,
            StoryVersionId = version.Id,
            ArtifactType = "story",
            OperationType = "partial_edit",
            OriginalContent = JsonSerializer.SerializeToElement(new
            {
                content = currentContent,
                start = input.Selection.Start,
                endExclusive = input.Selection.EndExclusive,
                selectedText
            }),
            SuggestedContent = JsonSerializer.SerializeToElement(new { content = result.Story.StorySections.FirstOrDefault()?.Content ?? string.Empty }),
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        var proposalId = await _proposalCache.CreateAsync(proposal);
        return new CreateProposalResponseDto { ProposalId = proposalId, Message = "AI story edit proposal is ready for preview." };
    }

    #endregion

    #region Vocabulary Review

    public async Task<VocabularyReviewDto> GetVocabularyForReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        await AuthorizeCreatorOperationAsync(userId, storyId, cancellationToken);
        await EnsureLearningModeAsync(userId, storyId, cancellationToken);
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("StoryVersion");

        var allItems = await _vocabRepo.FindAsync(v => v.StoryVersionId == version.Id, cancellationToken: cancellationToken);
        var items = allItems.Select(v => new DTOs.VocabularyItemDto { Id = v.Id, Term = v.Term, Definition = v.Definition,
            SourceQuote = v.SourceQuote, SourceStart = v.SourceStart, SourceEnd = v.SourceEnd, Example = v.Example }).ToList();

        return new VocabularyReviewDto
        {
            StoryId = storyId,
            VersionId = version.Id,
            Items = items
        };
    }

    public Task<VocabularyReviewDto> UpdateVocabularyAsync(int userId, int storyId, UpdateVocabularyRequestDto input, CancellationToken cancellationToken = default) =>
        CreatorPublicationGuard.WriteAsync(_unitOfWork, storyId, input.VersionId,
            () => UpdateVocabularyCoreAsync(userId, storyId, input, cancellationToken), cancellationToken);

    private async Task<VocabularyReviewDto> UpdateVocabularyCoreAsync(int userId, int storyId, UpdateVocabularyRequestDto input, CancellationToken cancellationToken)
    {
        await AuthorizeCreatorOperationAsync(userId, storyId, cancellationToken);
        await EnsureLearningModeAsync(userId, storyId, cancellationToken);
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.Id == input.VersionId && v.StoryId == storyId, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("StoryVersion");

        var normalizedTerms = input.Items.Select(item => item.Term.Trim()).ToArray();
        if (input.Items.Any(item => string.IsNullOrWhiteSpace(item.Term) || string.IsNullOrWhiteSpace(item.Definition)) ||
            normalizedTerms.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedTerms.Length)
        {
            throw new BadRequestException("Vocabulary terms and definitions are required, and terms must be unique.");
        }

        var storyContent = version.Content ?? string.Empty;
        if (normalizedTerms.Any(term => !storyContent.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BadRequestException("Every vocabulary term must occur in the story content.");
        }

        var existingItems = await _vocabRepo.FindAsync(v => v.StoryVersionId == input.VersionId, cancellationToken: cancellationToken);
        var existingIds = existingItems.Select(v => v.Id).ToHashSet();

        var inputIds = input.Items.Where(i => i.Id.HasValue).Select(i => i.Id!.Value).ToHashSet();

        // Delete removed items
        var toDelete = existingIds.Except(inputIds);
        foreach (var id in toDelete)
        {
            var item = existingItems.FirstOrDefault(v => v.Id == id);
            if (item != null) _vocabRepo.Delete(item);
        }

        // Update existing and add new
        foreach (var item in input.Items)
        {
            if (item.Id.HasValue)
            {
                var existing = existingItems.FirstOrDefault(v => v.Id == item.Id);
                if (existing != null)
                {
                    LearningArtifactDetails.Apply(existing, item, storyContent);
                    existing.Term = item.Term.Trim();
                    existing.Definition = item.Definition.Trim();
                    _vocabRepo.Update(existing);
                }
            }
            else
            {
                var created = new StoryVocabulary
                {
                    StoryVersionId = input.VersionId,
                    Term = item.Term.Trim(),
                    Definition = item.Definition.Trim()
                };
                LearningArtifactDetails.Apply(created, item, storyContent);
                await _vocabRepo.AddAsync(created, cancellationToken);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetVocabularyForReviewAsync(userId, storyId, cancellationToken);
    }

    public async Task<CreateProposalResponseDto> CreateVocabularyProposalAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        await AuthorizeCreatorOperationAsync(userId, storyId, cancellationToken);
        await EnsureLearningModeAsync(userId, storyId, cancellationToken);
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("StoryVersion");

        var story = new StoryContentDto
        {
            Title = version.Title,
            Lesson = version.Lesson ?? string.Empty,
            StorySections = new List<StorySectionDto>
            {
                new(1, string.Empty, version.Content ?? string.Empty)
            }
        };

        var vocabItems = await _vocabRepo.FindAsync(v => v.StoryVersionId == version.Id, cancellationToken: cancellationToken);
        var vocabDtos = vocabItems.Select(v => new GeneratedVocabularyItemDto(v.Term, v.Definition)).ToList();

        var audience = await _storyRepo.GetByIdAsync(storyId, cancellationToken) ?? throw new NotFoundException("Story", storyId);
        var request = new GenerateVocabularyRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Story = story,
            Language = audience.Language,
            AgeBand = audience.AgeBand
        };

        var result = await _aiClient.GenerateVocabularyAsync(request, cancellationToken);

        var proposal = new AIProposalDto
        {
            RequestedByUserId = userId,
            StoryId = storyId,
            StoryVersionId = version.Id,
            ArtifactType = "vocabulary",
            OperationType = "regenerate",
            OriginalContent = JsonSerializer.SerializeToElement(new { items = vocabDtos }, ProposalJsonOptions),
            SuggestedContent = JsonSerializer.SerializeToElement(new { items = result.Items }, ProposalJsonOptions),
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        var proposalId = await _proposalCache.CreateAsync(proposal);
        return new CreateProposalResponseDto { ProposalId = proposalId, Message = "AI vocabulary proposal is ready for preview." };
    }

    #endregion

    #region Quiz Review

    public async Task<QuizReviewDto> GetQuizForReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        await AuthorizeCreatorOperationAsync(userId, storyId, cancellationToken);
        await EnsureLearningModeAsync(userId, storyId, cancellationToken);
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("StoryVersion");

        var allItems = await _quizRepo.FindAsync(q => q.StoryVersionId == version.Id, cancellationToken: cancellationToken);
        var items = allItems.Select(q => new DTOs.QuizItemDto
        {
            Id = q.Id,
            Type = q.Type.ToString(),
            Question = q.Question,
            CorrectAnswer = q.CorrectAnswer,
            SourceQuote = q.SourceQuote, SourceStart = q.SourceStart, SourceEnd = q.SourceEnd,
            Explanation = q.Explanation, Hint = q.Hint, LearningGoal = q.LearningGoal,
            OptionFeedback = string.IsNullOrEmpty(q.OptionFeedbackJson) ? null : JsonSerializer.Deserialize<List<string>>(q.OptionFeedbackJson),
            Choices = string.IsNullOrEmpty(q.Choices) ? null : JsonSerializer.Deserialize<List<string>>(q.Choices)
        }).ToList();

        return new QuizReviewDto
        {
            StoryId = storyId,
            VersionId = version.Id,
            Items = items
        };
    }

    public Task<QuizReviewDto> UpdateQuizAsync(int userId, int storyId, UpdateQuizRequestDto input, CancellationToken cancellationToken = default) =>
        CreatorPublicationGuard.WriteAsync(_unitOfWork, storyId, input.VersionId,
            () => UpdateQuizCoreAsync(userId, storyId, input, cancellationToken), cancellationToken);

    private async Task<QuizReviewDto> UpdateQuizCoreAsync(int userId, int storyId, UpdateQuizRequestDto input, CancellationToken cancellationToken)
    {
        await AuthorizeCreatorOperationAsync(userId, storyId, cancellationToken);
        await EnsureLearningModeAsync(userId, storyId, cancellationToken);
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.Id == input.VersionId && v.StoryId == storyId, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("StoryVersion");

        foreach (var item in input.Items)
        {
            if (!Enum.TryParse<QuizType>(item.Type, true, out var type) || string.IsNullOrWhiteSpace(item.Question))
            {
                throw new BadRequestException("Every quiz item requires a valid type and question.");
            }

            if (type == QuizType.MultipleChoice &&
                (item.Choices is null || item.Choices.Count < 2 || string.IsNullOrWhiteSpace(item.CorrectAnswer) ||
                 !item.Choices.Contains(item.CorrectAnswer, StringComparer.OrdinalIgnoreCase)))
            {
                throw new BadRequestException("Multiple-choice items require at least two choices and a matching correct answer.");
            }

            if (type == QuizType.TrueFalse && !bool.TryParse(item.CorrectAnswer, out _))
            {
                throw new BadRequestException("True/false items require a boolean correct answer.");
            }

            if (type == QuizType.ShortAnswer && string.IsNullOrWhiteSpace(item.CorrectAnswer))
            {
                throw new BadRequestException("Short-answer items require a correct answer.");
            }
        }

        var existingItems = await _quizRepo.FindAsync(q => q.StoryVersionId == input.VersionId, cancellationToken: cancellationToken);
        var existingIds = existingItems.Select(q => q.Id).ToHashSet();

        var inputIds = input.Items.Where(i => i.Id.HasValue).Select(i => i.Id!.Value).ToHashSet();

        // Delete removed items
        var toDelete = existingIds.Except(inputIds);
        foreach (var id in toDelete)
        {
            var item = existingItems.FirstOrDefault(q => q.Id == id);
            if (item != null) _quizRepo.Delete(item);
        }

        // Update existing and add new
        foreach (var item in input.Items)
        {
            if (item.Id.HasValue)
            {
                var existing = existingItems.FirstOrDefault(q => q.Id == item.Id);
                if (existing != null)
                {
                    LearningArtifactDetails.Apply(existing, item, version.Content ?? "");
                    existing.Type = Enum.Parse<QuizType>(item.Type, true);
                    existing.Question = item.Question.Trim();
                    existing.CorrectAnswer = item.CorrectAnswer?.Trim();
                    existing.Choices = item.Choices != null ? JsonSerializer.Serialize(item.Choices) : null;
                    _quizRepo.Update(existing);
                }
            }
            else
            {
                var created = new QuizItem
                {
                    StoryVersionId = input.VersionId,
                    Type = Enum.Parse<QuizType>(item.Type, true),
                    Question = item.Question.Trim(),
                    CorrectAnswer = item.CorrectAnswer?.Trim(),
                    Choices = item.Choices != null ? JsonSerializer.Serialize(item.Choices) : null
                };
                LearningArtifactDetails.Apply(created, item, version.Content ?? "");
                await _quizRepo.AddAsync(created, cancellationToken);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetQuizForReviewAsync(userId, storyId, cancellationToken);
    }

    public async Task<CreateProposalResponseDto> CreateQuizProposalAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        await AuthorizeCreatorOperationAsync(userId, storyId, cancellationToken);
        await EnsureLearningModeAsync(userId, storyId, cancellationToken);
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("StoryVersion");

        var story = new StoryContentDto
        {
            Title = version.Title,
            Lesson = version.Lesson ?? string.Empty,
            StorySections = new List<StorySectionDto>
            {
                new(1, string.Empty, version.Content ?? string.Empty)
            }
        };

        var vocabItems = await _vocabRepo.FindAsync(v => v.StoryVersionId == version.Id, cancellationToken: cancellationToken);
        var vocabDtos = vocabItems.Select(v => new GeneratedVocabularyItemDto(v.Term, v.Definition)).ToList();

        var audience = await _storyRepo.GetByIdAsync(storyId, cancellationToken) ?? throw new NotFoundException("Story", storyId);
        var request = new GenerateQuizRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Story = story,
            Vocabulary = vocabDtos,
            Language = audience.Language,
            AgeBand = audience.AgeBand
        };

        var result = await _aiClient.GenerateQuizAsync(request, cancellationToken);

        var quizItems = result.Items.Select(i => new DTOs.QuizItemDto
        {
            Type = i.Type.ToString(),
            Question = i.Question,
            CorrectAnswer = i.CorrectAnswer,
            Choices = i.Options?.ToList()
        }).ToList();

        var proposal = new AIProposalDto
        {
            RequestedByUserId = userId,
            StoryId = storyId,
            StoryVersionId = version.Id,
            ArtifactType = "quiz",
            OperationType = "regenerate",
            OriginalContent = JsonSerializer.SerializeToElement(new { }),
            SuggestedContent = JsonSerializer.SerializeToElement(new { items = quizItems }, ProposalJsonOptions),
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        var proposalId = await _proposalCache.CreateAsync(proposal);
        return new CreateProposalResponseDto { ProposalId = proposalId, Message = "AI quiz proposal is ready for preview." };
    }

    #endregion

    #region Validation & Approval

    public async Task<bool> CompleteStoryReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        await AuthorizeCreatorOperationAsync(userId, storyId, cancellationToken);
        var story = await _storyRepo.GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story", storyId);
        if (StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story)) throw new ConflictException("PARENT_MANUAL_USE_AUTHORING_API");
        GenerationStoryAccess.EnsureSupported(story);
        await EnsureReviewPermissionAsync(story, userId, cancellationToken);
        if (story.OutputMode == StoryOutputMode.ReadingMediaOnly && story.Status != StoryStatus.ContentReview)
            throw new ConflictException("STORY_NOT_READY_FOR_REVIEW");
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: cancellationToken)
                      ?? throw new NotFoundException("StoryVersion");
        _reviewCompletionStore?.MarkStoryReviewed(storyId, version.Id, userId);
        return true;
    }

    public async Task<bool> CompleteVocabularyReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        await AuthorizeCreatorOperationAsync(userId, storyId, cancellationToken);
        await EnsureLearningModeAsync(userId, storyId, cancellationToken);
        var story = await _storyRepo.GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story", storyId);
        if (StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story)) throw new ConflictException("PARENT_MANUAL_USE_AUTHORING_API");
        GenerationStoryAccess.EnsureSupported(story);
        await EnsureReviewPermissionAsync(story, userId, cancellationToken);
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: cancellationToken)
                      ?? throw new NotFoundException("StoryVersion");
        _reviewCompletionStore?.MarkVocabularyReviewed(storyId, version.Id, userId);
        return true;
    }

    public async Task<bool> CompleteQuizReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        await AuthorizeCreatorOperationAsync(userId, storyId, cancellationToken);
        await EnsureLearningModeAsync(userId, storyId, cancellationToken);
        var story = await _storyRepo.GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story", storyId);
        if (StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story)) throw new ConflictException("PARENT_MANUAL_USE_AUTHORING_API");
        GenerationStoryAccess.EnsureSupported(story);
        await EnsureReviewPermissionAsync(story, userId, cancellationToken);
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: cancellationToken)
                      ?? throw new NotFoundException("StoryVersion");
        _reviewCompletionStore?.MarkQuizReviewed(storyId, version.Id, userId);
        return true;
    }


    public async Task<ValidationResultDto> ValidateAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        await AuthorizeCreatorOperationAsync(userId, storyId, cancellationToken);
        var checks = new List<ValidationCheckDto>();
        var issues = new List<string>();

        var story = await _storyRepo.FirstOrDefaultAsync(s => s.Id == storyId, cancellationToken: cancellationToken);
        if (story == null)
        {
            checks.Add(new ValidationCheckDto { Name = "story_exists", Passed = false, Message = "Story not found" });
            return new ValidationResultDto { CanApprove = false, Checks = checks, Issues = ["Story not found"] };
        }

        if (StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story)) throw new ConflictException("PARENT_MANUAL_USE_AUTHORING_API");
        var readingMediaOnly = story.OutputMode == StoryOutputMode.ReadingMediaOnly;

        var version = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: cancellationToken);
        if (version == null)
        {
            checks.Add(new ValidationCheckDto { Name = "story_complete", Passed = false, Message = "No version found" });
            issues.Add("Story version not found");
            return new ValidationResultDto { CanApprove = false, Checks = checks, Issues = issues };
        }

        // 1. Check story completeness
        var storyValid = !string.IsNullOrWhiteSpace(version.Title) &&
                         !string.IsNullOrWhiteSpace(version.Content) &&
                         !string.IsNullOrWhiteSpace(version.Lesson);
        checks.Add(new ValidationCheckDto { Name = "story_complete", Passed = storyValid, Message = storyValid ? null : "Title, Content, or Lesson is empty" });
        if (!storyValid) issues.Add("Story content is incomplete");

        var safetyPolicy = await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            policy => policy.ChildProfileId == story.ChildProfileId,
            cancellationToken: cancellationToken);
        if (story.Scope == StoryScope.Creator)
        {
            var context = await LoadCreatorContextAsync(story, cancellationToken);
            safetyPolicy = new SafetyPolicy
            {
                SafetyScoreThreshold = context.SafetyScoreThreshold,
                ReadabilityScoreThreshold = context.ReadabilityScoreThreshold,
                ParentalGateEnabled = true,
                RequiredApprovalMode = ApprovalMode.AlwaysManual
            };
            var prohibited = (context.BlockedCategoryTerms ?? context.BlockedCategoryCodes)
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Any(term => System.Text.RegularExpressions.Regex.IsMatch(version.Content ?? "",
                    $@"(?<![\p{{L}}\p{{N}}]){System.Text.RegularExpressions.Regex.Escape(term)}(?![\p{{L}}\p{{N}}])",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant));
            if (prohibited)
            {
                checks.Add(new ValidationCheckDto { Name = "creator_audience_policy", Passed = false, Message = "Nội dung thuộc nhóm bị chặn." });
                issues.Add("Nội dung thuộc nhóm bị chặn cho đối tượng độc giả.");
            }
        }

        // 2. Check Readability
        var readability = ReadabilityCalculator.EvaluateForProfile(
            version.Content,
            story.Language,
            story.ReadingLevel ?? 2,
            safetyPolicy?.ReadabilityScoreThreshold);
        checks.Add(new ValidationCheckDto
        {
            Name = "readability_valid",
            Passed = readability.Passed,
            Message = $"{readability.Metrics.Algorithm}: grade={readability.Metrics.Fkgl:F2}, ease={readability.Metrics.Fre:F2}"
        });
        if (!readability.Passed)
        {
            issues.Add($"Readability does not match Reading Level {story.ReadingLevel ?? 2} " +
                       $"(grade {readability.Metrics.Fkgl:F2}/{readability.MaximumGradeLevel:F2}, " +
                       $"ease {readability.Metrics.Fre:F2}/{readability.MinimumEaseScore:F2}, " +
                       $"average words {readability.Metrics.AverageWordsPerSentence:F2}/{readability.MaximumAverageWordsPerSentence:F2})");
        }

        // 3. Check Safety
        var safetyPassed = !GrayZoneGate.IsBlocked(story, version);
        if (!safetyPassed) issues.Add("SAFETY_MODERATION_REQUIRED");
        if (version.SafetyScore.HasValue && version.SafetyScore.Value <= 0)
        {
            safetyPassed = false;
            issues.Add("Story safety score indicates hard safety block.");
        }
        else if (safetyPolicy?.SafetyScoreThreshold.HasValue == true)
        {
            var threshold = safetyPolicy.SafetyScoreThreshold.Value;
            var currentScore = version.SafetyScore ?? 0m;
            if (currentScore < threshold)
            {
                safetyPassed = false;
                issues.Add($"Điểm an toàn ({currentScore:F2}) thấp hơn ngưỡng ({threshold:F2}).");
            }
        }

        if (safetyPolicy != null)
        {
            var policyCategories = await _unitOfWork.Repository<SafetyPolicyCategory>().FindAsync(
                spc => spc.SafetyPolicyId == safetyPolicy.Id && spc.Rule == PolicyRule.Blocked,
                cancellationToken: cancellationToken);
            if (policyCategories.Count > 0)
            {
                var catIds = policyCategories.Select(pc => pc.ContentCategoryId).ToList();
                var cats = await _unitOfWork.Repository<ContentCategory>().FindAsync(
                    c => catIds.Contains(c.Id) && c.IsActive, cancellationToken: cancellationToken);
                var terms = cats.SelectMany(c => new[] { c.DisplayName, c.Code })
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var hit = terms.FirstOrDefault(t => (version.Content ?? string.Empty).Contains(t, StringComparison.OrdinalIgnoreCase));
                if (hit != null)
                {
                    safetyPassed = false;
                    issues.Add($"Story content contains blocked term: '{hit}'.");
                }
            }
        }
        checks.Add(new ValidationCheckDto
        {
            Name = "safety_passed",
            Passed = safetyPassed,
            Message = safetyPassed ? "Safety checks passed" : "Safety requirements not met"
        });

        // 4. Check Profile Decision Resolution
        var profilePassed = true;
        if (story.Source == StorySource.Manual)
        {
            var cachedEval = _evaluationCache?.Get(storyId, version.Id);
            if (cachedEval != null)
            {
                if (cachedEval.Decision == ExistingStoryDecision.Blocked || !cachedEval.CanKeepOriginal)
                {
                    profilePassed = false;
                    issues.Add("Existing story evaluation decision is Blocked; safety issues must be resolved.");
                }
                else if (cachedEval.Decision == ExistingStoryDecision.AdaptRecommended && version.EditType == VersionEditType.Initial)
                {
                    profilePassed = false;
                    issues.Add("Existing story has AdaptRecommended decision; requires adaptation or manual edit before approval.");
                }
            }
        }
        checks.Add(new ValidationCheckDto
        {
            Name = "profile_decision_resolved",
            Passed = profilePassed,
            Message = profilePassed ? "Profile decision resolved" : "Profile decision unresolved"
        });

        // 5. Check vocabulary
        var vocabularyItems = await _vocabRepo.FindAsync(v => v.StoryVersionId == version.Id, cancellationToken: cancellationToken);
        var vocabularyTerms = vocabularyItems.Select(item => item.Term.Trim()).ToArray();
        var vocabValid = vocabularyItems.Count >= 5 &&
                         vocabularyItems.All(item => !string.IsNullOrWhiteSpace(item.Term) && !string.IsNullOrWhiteSpace(item.Definition)) &&
                         vocabularyTerms.Distinct(StringComparer.OrdinalIgnoreCase).Count() == vocabularyTerms.Length &&
                         vocabularyTerms.All(term => (version.Content ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        checks.Add(new ValidationCheckDto { Name = "vocabulary_valid", Passed = readingMediaOnly || vocabValid, Message = readingMediaOnly ? "skipped" : $"{vocabularyItems.Count} items" });
        if (!readingMediaOnly && !vocabValid) issues.Add($"Vocabulary needs at least 5 unique, non-empty terms that occur in the story (current: {vocabularyItems.Count})");

        // 6. Check quiz
        var quizItems = await _quizRepo.FindAsync(q => q.StoryVersionId == version.Id, cancellationToken: cancellationToken);
        var quizValid = quizItems.Count >= 3 && quizItems.All(IsValidQuizItem);
        var hasAllTypes = Enum.GetValues<QuizType>().All(t => quizItems.Any(q => q.Type == t));
        var quizPassed = quizValid && hasAllTypes;
        checks.Add(new ValidationCheckDto { Name = "quiz_valid", Passed = readingMediaOnly || quizPassed, Message = readingMediaOnly ? "skipped" : quizPassed ? $"{quizItems.Count} questions, all types" : "Missing types" });
        if (!readingMediaOnly && !quizPassed) issues.Add("Quiz needs at least 3 valid questions with all types and valid answers");

        // 7. Check Artifact Bindings
        var bindingsPassed = vocabularyItems.All(v => v.StoryVersionId == version.Id) &&
                             quizItems.All(q => q.StoryVersionId == version.Id);
        checks.Add(new ValidationCheckDto
        {
            Name = "artifact_bindings_valid",
            Passed = readingMediaOnly || bindingsPassed,
            Message = readingMediaOnly ? "skipped" : bindingsPassed ? "All artifacts bound to current version" : "Artifacts binding mismatch"
        });
        if (!readingMediaOnly && !bindingsPassed) issues.Add("Artifacts must be bound to the current story version.");

        // 8. Check Review Complete
        var isMarkedComplete = _reviewCompletionStore?.IsReviewCompleted(storyId, version.Id) ?? false;
        var structurallyComplete = storyValid && vocabValid && quizPassed;
        var autoApproval = !story.EverGrayZone && safetyPolicy is { ParentalGateEnabled: false, RequiredApprovalMode: ApprovalMode.AutoPublishOnThreshold };
        var reviewComplete = readingMediaOnly
            ? (_reviewCompletionStore?.IsStoryReviewed(storyId, version.Id) ?? false) || autoApproval
            : isMarkedComplete || structurallyComplete;
        checks.Add(new ValidationCheckDto
        {
            Name = "review_complete",
            Passed = reviewComplete,
            Message = reviewComplete ? "Review completed" : "Review incomplete"
        });
        if (!reviewComplete) issues.Add("Required review is incomplete");

        var canApprove = !checks.Any(check => check.Name == "creator_audience_policy" && !check.Passed) && storyValid &&
                         readability.Passed &&
                         safetyPassed &&
                         profilePassed &&
                         (readingMediaOnly || vocabValid && quizPassed && bindingsPassed) &&
                         reviewComplete;

        return new ValidationResultDto
        {
            CanApprove = canApprove && story.Scope != StoryScope.Creator,
            CanConfirmContent = canApprove && story.Scope == StoryScope.Creator && story.Status == StoryStatus.ContentReview,
            Checks = checks,
            Issues = issues
        };
    }

    public async Task<ApproveResponseDto> ApproveAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        await QueueReviewedMediaAsync(userId, storyId, false, null, cancellationToken);
        return new ApproveResponseDto
        {
            Success = true,
            StoryId = storyId,
            Status = "Approved",
            ApprovedAt = DateTime.UtcNow
        };
    }

    public async Task<ConfirmCreatorContentResponseDto> ConfirmCreatorContentAsync(
        int userId, int storyId, ConfirmCreatorContentRequestDto input, CancellationToken cancellationToken = default)
    {
        if (input.VersionId <= 0) throw new BadRequestException("VersionId phải lớn hơn 0.");
        var versionId = await QueueReviewedMediaAsync(userId, storyId, true, input.VersionId, cancellationToken);
        return new ConfirmCreatorContentResponseDto
        {
            StoryId = storyId,
            VersionId = versionId,
            Status = "MediaProcessing",
            ConfirmedAt = DateTime.UtcNow
        };
    }

    // Content confirmation queues media only; public review remains owned by Moderator.
    private async Task<int> QueueReviewedMediaAsync(int userId, int storyId, bool creatorConfirmation,
        int? expectedVersionId, CancellationToken cancellationToken)
    {
        await AuthorizeCreatorOperationAsync(userId, storyId, cancellationToken);
        var storyForAuthorization = await _storyRepo.FirstOrDefaultAsync(
            s => s.Id == storyId, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("Story");
        await EnsureReviewPermissionAsync(storyForAuthorization, userId, cancellationToken);
        if (creatorConfirmation != (storyForAuthorization.Scope == StoryScope.Creator))
            throw new ForbiddenException("Creator phải xác nhận nội dung qua API confirm-content; duyệt công khai thuộc Moderator.");
        if (storyForAuthorization.Status != StoryStatus.ContentReview)
            throw new ConflictException("INVALID_STORY_STATUS");

        int? evaluatedVersionId = null;
        if (_moderation?.IsConfigured == true)
        {
            var source = await _versionRepo.FirstOrDefaultAsync(x => x.StoryId == storyId && x.IsCurrent, cancellationToken: cancellationToken)
                ?? throw new ConflictException("SAVED_VERSION_REQUIRED");
            if (expectedVersionId.HasValue && source.Id != expectedVersionId.Value) throw new ConflictException("STALE_STORY_VERSION");
            await _moderation.CheckAsync(userId, storyId, source.Id, cancellationToken);
            evaluatedVersionId = source.Id;
        }
        // Preserve Parent preflight behavior; Creator validation must follow the locked version check.
        if (!creatorConfirmation)
        {
            var validation = await ValidateAsync(userId, storyId, cancellationToken);
            if (!validation.CanApprove)
                throw new BadRequestException($"Content is not ready for media: {string.Join(", ", validation.Issues)}");
        }

        int versionId = 0;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, cancellationToken);
            var story = await _storyRepo.FirstOrDefaultAsync(s => s.Id == storyId, cancellationToken: cancellationToken)
                ?? throw new NotFoundException("Story");
            if (StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story)) throw new ConflictException("PARENT_MANUAL_USE_AUTHORING_API");
            GenerationStoryAccess.EnsureSupported(story);
            await EnsureReviewPermissionAsync(story, userId, cancellationToken);
            if (story.Status != StoryStatus.ContentReview)
                throw new ConflictException("INVALID_STORY_STATUS");
            var approvedVersion = await _versionRepo.FirstOrDefaultAsync(
                v => v.StoryId == storyId && v.IsCurrent && v.Content != null,
                cancellationToken: cancellationToken)
                ?? throw new BadRequestException("Cannot prepare media without a current canonical story version.");

            if (expectedVersionId.HasValue && approvedVersion.Id != expectedVersionId.Value
                || evaluatedVersionId.HasValue && approvedVersion.Id != evaluatedVersionId.Value)
                throw new ConflictException("STALE_STORY_VERSION");
            if (creatorConfirmation) await RequireActiveCreatorAsync(story, userId, cancellationToken);
            await CreatorPublicationGuard.EnsureWritableAsync(_unitOfWork, storyId, expectedVersionId, cancellationToken);
            var lockedValidation = await ValidateAsync(userId, storyId, cancellationToken);
            if (!(creatorConfirmation ? lockedValidation.CanConfirmContent : lockedValidation.CanApprove))
                throw new BadRequestException($"Content is not ready for media: {string.Join(", ", lockedValidation.Issues)}");
            versionId = approvedVersion.Id;
            var jobs = _unitOfWork.Repository<StoryGenerationJob>();
            if (await jobs.ExistsAsync(j =>
                    j.StoryId == storyId && j.StoryVersionId == approvedVersion.Id &&
                    j.Operation == GenerationJobOperation.GenerateMediaPackage,
                    cancellationToken))
                throw new ConflictException("MEDIA_JOB_ALREADY_EXISTS");

            var sourceJob = (await jobs.FindAsync(j =>
                    j.StoryId == storyId && j.StoryVersionId == approvedVersion.Id &&
                    j.GenerationRequestId != null,
                    cancellationToken: cancellationToken))
                .OrderByDescending(j => j.Id)
                .FirstOrDefault();
            var acceptedRequest = sourceJob?.GenerationRequestId is null
                ? (await _unitOfWork.Repository<StoryGenerationRequest>().FindAsync(
                        request => request.StoryId == storyId &&
                                   request.Status == GenerationInputStatus.InputAccepted,
                        cancellationToken: cancellationToken))
                    .OrderByDescending(request => request.Id).FirstOrDefault()
                : null;
            StoryPlatform.Application.Features.MediaGeneration.Services.MediaPackageConfirmation.Invalidate(approvedVersion);
            _versionRepo.Update(approvedVersion);
            await jobs.AddAsync(new StoryGenerationJob
            {
                StoryId = storyId,
                GenerationRequestId = sourceJob?.GenerationRequestId ?? acceptedRequest?.Id,
                StoryVersionId = approvedVersion.Id,
                BaseStoryVersionId = approvedVersion.Id,
                RequestedByUserId = userId,
                OperationKey = $"p5:{storyId}:{approvedVersion.Id}",
                Operation = GenerationJobOperation.GenerateMediaPackage,
                Stage = JobStage.MediaPending,
                Status = GenerationJobStatus.Pending,
                AttemptNo = 0,
                MaxAttempts = 3,
                StartedAt = DateTime.UtcNow
            }, cancellationToken);

            story.Status = creatorConfirmation ? StoryStatus.MediaProcessing : StoryStatus.Approved;
            _storyRepo.Update(story);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return versionId;
    }

    public async Task<ApproveResponseDto?> EvaluateAndApplyAutoPublishAsync(
        int storyId, CancellationToken cancellationToken = default)
    {
        var story = await _storyRepo.FirstOrDefaultAsync(s => s.Id == storyId, cancellationToken: cancellationToken);
        if (story is null || story.Scope != StoryScope.Parent || story.EverGrayZone || StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story) || story.Status != StoryStatus.ContentReview)
            return null;

        var safetyPolicy = await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            policy => policy.ChildProfileId == story.ChildProfileId,
            cancellationToken: cancellationToken);

        // Nếu ParentalGateEnabled = true hoặc RequiredApprovalMode != AutoPublishOnThreshold -> fallback manual review
        if (safetyPolicy is null ||
            safetyPolicy.ParentalGateEnabled ||
            safetyPolicy.RequiredApprovalMode != ApprovalMode.AutoPublishOnThreshold)
        {
            return null;
        }

        var validation = await ValidateAsync(story.AuthorUserId, storyId, cancellationToken);
        if (!validation.CanApprove)
        {
            // Fallback manual review: giữ ContentReview
            return null;
        }

        // Tự động phê duyệt và chuyển Phase 5 Media
        return await ApproveAsync(story.AuthorUserId, storyId, cancellationToken);
    }

    public async Task<ArchiveResponseDto> ArchiveAsync(int userId, int storyId, ArchiveRequestDto input, CancellationToken cancellationToken = default)
    {
        await AuthorizeCreatorOperationAsync(userId, storyId, cancellationToken);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, cancellationToken);
            var story = await _storyRepo.FirstOrDefaultAsync(
                s => s.Id == storyId, cancellationToken: cancellationToken)
                ?? throw new NotFoundException("Story");
            if (StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story)) throw new ConflictException("PARENT_MANUAL_USE_AUTHORING_API");
            GenerationStoryAccess.EnsureSupported(story);
            await EnsureReviewPermissionAsync(story, userId, cancellationToken);
            story.Status = StoryStatus.Archived;
            _storyRepo.Update(story);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        return new ArchiveResponseDto
        {
            Success = true,
            StoryId = storyId,
            Status = "Archived"
        };
    }

    private async Task EnsureLearningModeAsync(int userId, int storyId, CancellationToken cancellationToken)
    {
        var story = await _storyRepo.GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story", storyId);
        if (StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story)) throw new ConflictException("PARENT_MANUAL_USE_AUTHORING_API");
        GenerationStoryAccess.EnsureSupported(story);
        await EnsureReviewPermissionAsync(story, userId, cancellationToken);
        if (story.OutputMode != StoryOutputMode.Learning)
            throw new ConflictException("LEARNING_ARTIFACT_NOT_ENABLED");
    }

    private async Task<StoryPlatform.Application.Features.AIStoryInput.Models.AIStoryInputContextSnapshot> LoadCreatorContextAsync(Story story, CancellationToken ct)
    {
        var request = (await _unitOfWork.Repository<StoryGenerationRequest>().FindAsync(
            x => x.StoryId == story.Id && x.Status == GenerationInputStatus.InputAccepted,
            cancellationToken: ct)).OrderByDescending(x => x.Id).FirstOrDefault()
            ?? throw new ConflictException("CREATOR_CONTEXT_MISSING");
        var context = JsonSerializer.Deserialize<StoryPlatform.Application.Features.AIStoryInput.Models.AIStoryInputContextSnapshot>(
            request.ContextSnapshotJson, ProposalJsonOptions) ?? throw new ConflictException("CREATOR_CONTEXT_MISSING");
        GenerationStoryAccess.ValidateSnapshot(story, context);
        return context;
    }

    private async Task RequireActiveCreatorAsync(Story story, int userId, CancellationToken ct)
    {
        GenerationStoryAccess.EnsureSupported(story);
        if (story.AuthorUserId != userId) throw new NotFoundException("Story", story.Id);
        // Do not reuse a tracked account loaded before the provider call.
        var user = await _unitOfWork.Repository<UserAccount>().FirstOrDefaultAsync(x => x.Id == userId, cancellationToken: ct);
        if (user is null || user.IsDeleted || user.Role != UserRole.ContentCreator || user.Status != AccountStatus.Active ||
            user.LockedUntil > DateTime.UtcNow) throw new ForbiddenException();
    }

    private async Task<StoryVersion> RequireEditableCreatorVersionAsync(Story story, int userId, int versionId, CancellationToken ct)
    {
        await RequireActiveCreatorAsync(story, userId, ct);
        if (story.Status != StoryStatus.ContentReview) throw new ConflictException("INVALID_STORY_STATUS");
        var current = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == story.Id && v.IsCurrent && !v.IsDeleted,
            cancellationToken: ct);
        if (current is null || current.Id != versionId) throw new ConflictException("STALE_STORY_VERSION");
        await CreatorPublicationGuard.EnsureWritableAsync(_unitOfWork, story.Id, versionId, ct);
        if (await _unitOfWork.Repository<StoryGenerationJob>().ExistsAsync(j => j.StoryId == story.Id &&
            j.StoryVersionId == versionId && j.Operation == GenerationJobOperation.GenerateMediaPackage &&
            (j.Status == GenerationJobStatus.Pending || j.Status == GenerationJobStatus.Processing), ct))
            throw new ConflictException("MEDIA_JOB_ACTIVE");
        return current;
    }

    private async Task AuthorizeCreatorOperationAsync(int userId, int storyId, CancellationToken ct)
    {
        var story = await _storyRepo.GetByIdAsync(storyId, ct);
        if (story?.Scope == StoryScope.Creator)
            await GenerationStoryAccess.AuthorizeCreatorAsync(_unitOfWork, story, userId, ct);
    }

    private async Task EnsureReviewPermissionAsync(
        Story story, int userId, CancellationToken cancellationToken)
    {
        if (StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story)) throw new ConflictException("PARENT_MANUAL_USE_AUTHORING_API");
        GenerationStoryAccess.EnsureSupported(story);
        if (await GenerationStoryAccess.AuthorizeCreatorAsync(_unitOfWork, story, userId, cancellationToken)) return;
        if (!await ChildOwnership.IsOwnerAsync(_unitOfWork, story.ChildProfileId, userId, cancellationToken))
            throw new ForbiddenException("Bạn không có quyền duyệt hoặc lưu trữ Story này.");
    }

    private static bool IsValidQuizItem(QuizItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Question))
        {
            return false;
        }

        return item.Type switch
        {
            QuizType.MultipleChoice => HasValidMultipleChoiceAnswer(item),
            QuizType.TrueFalse => bool.TryParse(item.CorrectAnswer, out _),
            QuizType.ShortAnswer => !string.IsNullOrWhiteSpace(item.CorrectAnswer),
            _ => false
        };
    }

    private static bool HasValidMultipleChoiceAnswer(QuizItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Choices) || string.IsNullOrWhiteSpace(item.CorrectAnswer))
        {
            return false;
        }

        try
        {
            var choices = JsonSerializer.Deserialize<List<string>>(item.Choices) ?? [];
            return choices.Count >= 2 && choices.Contains(item.CorrectAnswer, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    #endregion
}
