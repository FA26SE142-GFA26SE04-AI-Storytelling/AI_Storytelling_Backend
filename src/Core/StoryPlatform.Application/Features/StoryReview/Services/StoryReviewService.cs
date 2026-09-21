using System.Text.Json;
using System.Linq;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentGeneration.Quality;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Application.Features.StoryReview.Interfaces;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryReview.Services;

public sealed class StoryReviewService : IStoryReviewService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAIStoryGenerationClient _aiClient;
    private readonly IProposalCache _proposalCache;
    private readonly IGenericRepository<Story> _storyRepo;
    private readonly IGenericRepository<StoryVersion> _versionRepo;
    private readonly IGenericRepository<StoryVocabulary> _vocabRepo;
    private readonly IGenericRepository<QuizItem> _quizRepo;
    private readonly IGenericRepository<DiscussionQuestion> _discussionRepo;

    public StoryReviewService(
        IUnitOfWork unitOfWork,
        IAIStoryGenerationClient aiClient,
        IProposalCache proposalCache)
    {
        _unitOfWork = unitOfWork;
        _aiClient = aiClient;
        _proposalCache = proposalCache;
        _storyRepo = unitOfWork.Repository<Story>();
        _versionRepo = unitOfWork.Repository<StoryVersion>();
        _vocabRepo = unitOfWork.Repository<StoryVocabulary>();
        _quizRepo = unitOfWork.Repository<QuizItem>();
        _discussionRepo = unitOfWork.Repository<DiscussionQuestion>();
    }

    #region Package

    public async Task<ReviewPackageDto> GetReviewPackageAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        var story = await _storyRepo.FirstOrDefaultAsync(s => s.Id == storyId, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("Story");

        var version = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("StoryVersion");

        var vocabCount = await _vocabRepo.CountAsync(v => v.StoryVersionId == version.Id, cancellationToken);
        var quizCount = await _quizRepo.CountAsync(q => q.StoryVersionId == version.Id, cancellationToken);
        var discussionCount = await _discussionRepo.CountAsync(d => d.StoryVersionId == version.Id, cancellationToken);
        var readability = ReadabilityCalculator.Calculate(version.Content, story.Language);

        return new ReviewPackageDto
        {
            StoryId = storyId,
            StoryVersionId = version.Id,
            StoryStatus = story.Status.ToString(),
            Title = version.Title,
            Content = version.Content ?? string.Empty,
            Lesson = version.Lesson ?? string.Empty,
            ReadabilityAlgorithm = readability.Algorithm,
            ReadabilityFkgl = version.ReadabilityFkgl ?? readability.Fkgl,
            ReadabilityFre = version.ReadabilityFre ?? readability.Fre,
            Vocabulary = new ArtifactStatusDto { State = vocabCount > 0 ? "completed" : "pending", ItemCount = vocabCount },
            Quiz = new ArtifactStatusDto { State = quizCount > 0 ? "completed" : "pending", ItemCount = quizCount },
            Discussion = new ArtifactStatusDto { State = discussionCount > 0 ? "completed" : "pending", ItemCount = discussionCount },
            CanEdit = true,
            CanApprove = story.Status == StoryStatus.ContentReview,
            CanArchive = story.Status == StoryStatus.ContentReview
        };
    }

    #endregion

    #region Story Review

    public async Task<StoryReviewDto> GetStoryForReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        var story = await _storyRepo.GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story");
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("StoryVersion");
        var readability = ReadabilityCalculator.Calculate(version.Content, story.Language);

        return new StoryReviewDto
        {
            StoryId = storyId,
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
        if (string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Content) || string.IsNullOrWhiteSpace(input.Lesson))
        {
            throw new BadRequestException("Title, content and lesson are required.");
        }

        // Phase 4 hardening: KHÔNG ghi đè current version.
        // Mọi chỉnh sửa phải tạo StoryVersion mới (EditType = HumanEdited) để bảo toàn audit trail.
        var story = await _storyRepo.GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story");
        await EnsureReviewPermissionAsync(story, userId, cancellationToken);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, cancellationToken);

            var currentVersion = await _versionRepo.FirstOrDefaultAsync(
                    v => v.Id == input.VersionId && v.StoryId == storyId && v.IsCurrent,
                    cancellationToken: cancellationToken)
                ?? throw new NotFoundException("StoryVersion");

            // Tạo version mới với EditType = HumanEdited, version_no = current + 1.
            var versions = await _versionRepo.FindAsync(
                v => v.StoryId == storyId, cancellationToken: cancellationToken);
            var nextVersionNo = versions.Select(v => v.VersionNo).DefaultIfEmpty().Max() + 1;

            currentVersion.IsCurrent = false;
            _versionRepo.Update(currentVersion);
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
                IsCurrent = true,
                OutlineApprovedAt = currentVersion.OutlineApprovedAt,
                OutlineApprovedByUserId = currentVersion.OutlineApprovedByUserId
            };
            await _versionRepo.AddAsync(newVersion, cancellationToken);

            // Đồng bộ Story header cho khớp với current version.
            story.Title = newVersion.Title;
            story.Content = newVersion.Content;
            story.MoralLesson = newVersion.Lesson;
            _storyRepo.Update(story);

            // Artifact revalidation: vocab/quiz/discussion đã sinh cho version cũ không
            // còn phù hợp -> đánh dấu job cũ là stale. Phase 3 worker sẽ tự skip job cũ
            // (qua StoryVersionId != current), và controller Phase 4 sẽ yêu cầu revalidate.
            // Ở đây ta KHÔNG xoá artifact cũ để giữ audit trail; chỉ đảm bảo chúng
            // không được dùng lại cho version mới (worker đã check version.IsCurrent).

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return new StoryReviewDto
            {
                StoryId = storyId,
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
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    public async Task<CreateProposalResponseDto> CreateProposalAsync(int userId, int storyId, PartialEditRequestDto input, CancellationToken cancellationToken = default)
    {
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

        var request = new RefineStoryContentRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Story = story,
            Language = "vi",
            AgeBand = "6-8",
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
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("StoryVersion");

        var allItems = await _vocabRepo.FindAsync(v => v.StoryVersionId == version.Id, cancellationToken: cancellationToken);
        var items = allItems.Select(v => new DTOs.VocabularyItemDto { Id = v.Id, Term = v.Term, Definition = v.Definition }).ToList();

        return new VocabularyReviewDto
        {
            StoryId = storyId,
            VersionId = version.Id,
            Items = items
        };
    }

    public async Task<VocabularyReviewDto> UpdateVocabularyAsync(int userId, int storyId, UpdateVocabularyRequestDto input, CancellationToken cancellationToken = default)
    {
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
                    existing.Term = item.Term.Trim();
                    existing.Definition = item.Definition.Trim();
                    _vocabRepo.Update(existing);
                }
            }
            else
            {
                await _vocabRepo.AddAsync(new StoryVocabulary
                {
                    StoryVersionId = input.VersionId,
                    Term = item.Term.Trim(),
                    Definition = item.Definition.Trim()
                }, cancellationToken);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetVocabularyForReviewAsync(userId, storyId, cancellationToken);
    }

    public async Task<CreateProposalResponseDto> CreateVocabularyProposalAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
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

        var request = new GenerateVocabularyRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Story = story,
            Language = "vi",
            AgeBand = "6-8"
        };

        var result = await _aiClient.GenerateVocabularyAsync(request, cancellationToken);

        var proposal = new AIProposalDto
        {
            RequestedByUserId = userId,
            StoryId = storyId,
            StoryVersionId = version.Id,
            ArtifactType = "vocabulary",
            OperationType = "regenerate",
            OriginalContent = JsonSerializer.SerializeToElement(new { items = vocabDtos }),
            SuggestedContent = JsonSerializer.SerializeToElement(new { items = result.Items }),
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
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("StoryVersion");

        var allItems = await _quizRepo.FindAsync(q => q.StoryVersionId == version.Id, cancellationToken: cancellationToken);
        var items = allItems.Select(q => new DTOs.QuizItemDto
        {
            Id = q.Id,
            Type = q.Type.ToString(),
            Question = q.Question,
            CorrectAnswer = q.CorrectAnswer,
            Choices = string.IsNullOrEmpty(q.Choices) ? null : JsonSerializer.Deserialize<List<string>>(q.Choices)
        }).ToList();

        return new QuizReviewDto
        {
            StoryId = storyId,
            VersionId = version.Id,
            Items = items
        };
    }

    public async Task<QuizReviewDto> UpdateQuizAsync(int userId, int storyId, UpdateQuizRequestDto input, CancellationToken cancellationToken = default)
    {
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
                    existing.Type = Enum.Parse<QuizType>(item.Type, true);
                    existing.Question = item.Question.Trim();
                    existing.CorrectAnswer = item.CorrectAnswer?.Trim();
                    existing.Choices = item.Choices != null ? JsonSerializer.Serialize(item.Choices) : null;
                    _quizRepo.Update(existing);
                }
            }
            else
            {
                await _quizRepo.AddAsync(new QuizItem
                {
                    StoryVersionId = input.VersionId,
                    Type = Enum.Parse<QuizType>(item.Type, true),
                    Question = item.Question.Trim(),
                    CorrectAnswer = item.CorrectAnswer?.Trim(),
                    Choices = item.Choices != null ? JsonSerializer.Serialize(item.Choices) : null
                }, cancellationToken);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetQuizForReviewAsync(userId, storyId, cancellationToken);
    }

    public async Task<CreateProposalResponseDto> CreateQuizProposalAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
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

        var request = new GenerateQuizRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Story = story,
            Vocabulary = vocabDtos,
            Language = "vi",
            AgeBand = "6-8"
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
            SuggestedContent = JsonSerializer.SerializeToElement(new { items = quizItems }),
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        var proposalId = await _proposalCache.CreateAsync(proposal);
        return new CreateProposalResponseDto { ProposalId = proposalId, Message = "AI quiz proposal is ready for preview." };
    }

    #endregion

    #region Discussion Review

    public async Task<DiscussionReviewDto> GetDiscussionForReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("StoryVersion");

        var allItems = await _discussionRepo.FindAsync(d => d.StoryVersionId == version.Id, cancellationToken: cancellationToken);
        var items = allItems.Select(d => new DiscussionItemDto
        {
            Id = d.Id,
            Question = d.Question,
            IsMoralLesson = d.IsMoralLesson
        }).ToList();

        return new DiscussionReviewDto
        {
            StoryId = storyId,
            VersionId = version.Id,
            Items = items
        };
    }

    public async Task<DiscussionReviewDto> UpdateDiscussionAsync(int userId, int storyId, UpdateDiscussionRequestDto input, CancellationToken cancellationToken = default)
    {
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.Id == input.VersionId && v.StoryId == storyId, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("StoryVersion");

        if (input.Items.Count < 2 || input.Items.Any(item => string.IsNullOrWhiteSpace(item.Question)))
        {
            throw new BadRequestException("Discussion requires at least two non-empty questions.");
        }

        var existingItems = await _discussionRepo.FindAsync(d => d.StoryVersionId == input.VersionId, cancellationToken: cancellationToken);
        var existingIds = existingItems.Select(d => d.Id).ToHashSet();

        var inputIds = input.Items.Where(i => i.Id.HasValue).Select(i => i.Id!.Value).ToHashSet();

        // Delete removed items
        var toDelete = existingIds.Except(inputIds);
        foreach (var id in toDelete)
        {
            var item = existingItems.FirstOrDefault(d => d.Id == id);
            if (item != null) _discussionRepo.Delete(item);
        }

        // Update existing and add new
        foreach (var item in input.Items)
        {
            if (item.Id.HasValue)
            {
                var existing = existingItems.FirstOrDefault(d => d.Id == item.Id);
                if (existing != null)
                {
                    existing.Question = item.Question.Trim();
                    existing.IsMoralLesson = item.IsMoralLesson;
                    _discussionRepo.Update(existing);
                }
            }
            else
            {
                await _discussionRepo.AddAsync(new DiscussionQuestion
                {
                    StoryVersionId = input.VersionId,
                    Question = item.Question.Trim(),
                    IsMoralLesson = item.IsMoralLesson
                }, cancellationToken);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetDiscussionForReviewAsync(userId, storyId, cancellationToken);
    }

    public async Task<CreateProposalResponseDto> CreateDiscussionProposalAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
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

        var request = new GenerateDiscussionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Story = story,
            Language = "vi",
            AgeBand = "6-8"
        };

        var result = await _aiClient.GenerateDiscussionAsync(request, cancellationToken);

        var discussionItems = result.Items.Select(i => new DiscussionItemDto
        {
            Question = i.Question,
            IsMoralLesson = false
        }).ToList();

        var proposal = new AIProposalDto
        {
            RequestedByUserId = userId,
            StoryId = storyId,
            StoryVersionId = version.Id,
            ArtifactType = "discussion",
            OperationType = "regenerate",
            OriginalContent = JsonSerializer.SerializeToElement(new { }),
            SuggestedContent = JsonSerializer.SerializeToElement(new { items = discussionItems }),
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        var proposalId = await _proposalCache.CreateAsync(proposal);
        return new CreateProposalResponseDto { ProposalId = proposalId, Message = "AI discussion proposal is ready for preview." };
    }

    #endregion

    #region Validation & Approval

    public async Task<ValidationResultDto> ValidateAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        var checks = new List<ValidationCheckDto>();
        var issues = new List<string>();

        var story = await _storyRepo.FirstOrDefaultAsync(s => s.Id == storyId, cancellationToken: cancellationToken);
        if (story == null)
        {
            checks.Add(new ValidationCheckDto { Name = "story_exists", Passed = false, Message = "Story not found" });
            return new ValidationResultDto { CanApprove = false, Checks = checks, Issues = ["Story not found"] };
        }

        var version = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: cancellationToken);
        if (version == null)
        {
            checks.Add(new ValidationCheckDto { Name = "story_complete", Passed = false, Message = "No version found" });
            issues.Add("Story version not found");
            return new ValidationResultDto { CanApprove = false, Checks = checks, Issues = issues };
        }

        // Check story completeness
        var storyValid = !string.IsNullOrWhiteSpace(version.Title) &&
                         !string.IsNullOrWhiteSpace(version.Content) &&
                         !string.IsNullOrWhiteSpace(version.Lesson);
        checks.Add(new ValidationCheckDto { Name = "story_complete", Passed = storyValid, Message = storyValid ? null : "Title, Content, or Lesson is empty" });
        if (!storyValid) issues.Add("Story content is incomplete");

        var safetyPolicy = await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            policy => policy.ChildProfileId == story.ChildProfileId,
            cancellationToken: cancellationToken);
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

        // Check vocabulary
        var vocabularyItems = await _vocabRepo.FindAsync(v => v.StoryVersionId == version.Id, cancellationToken: cancellationToken);
        var vocabularyTerms = vocabularyItems.Select(item => item.Term.Trim()).ToArray();
        var vocabValid = vocabularyItems.Count >= 5 &&
                         vocabularyItems.All(item => !string.IsNullOrWhiteSpace(item.Term) && !string.IsNullOrWhiteSpace(item.Definition)) &&
                         vocabularyTerms.Distinct(StringComparer.OrdinalIgnoreCase).Count() == vocabularyTerms.Length &&
                         vocabularyTerms.All(term => (version.Content ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        checks.Add(new ValidationCheckDto { Name = "vocabulary_valid", Passed = vocabValid, Message = $"{vocabularyItems.Count} items" });
        if (!vocabValid) issues.Add($"Vocabulary needs at least 5 unique, non-empty terms that occur in the story (current: {vocabularyItems.Count})");

        // Check quiz
        var quizItems = await _quizRepo.FindAsync(q => q.StoryVersionId == version.Id, cancellationToken: cancellationToken);
        var quizValid = quizItems.Count >= 3 && quizItems.All(IsValidQuizItem);
        var hasAllTypes = Enum.GetValues<QuizType>().All(t => quizItems.Any(q => q.Type == t));
        var quizPassed = quizValid && hasAllTypes;
        checks.Add(new ValidationCheckDto { Name = "quiz_valid", Passed = quizPassed, Message = quizPassed ? $"{quizItems.Count} questions, all types" : "Missing types" });
        if (!quizPassed) issues.Add("Quiz needs at least 3 valid questions with all types and valid answers");

        // Check discussion
        var discussionItems = await _discussionRepo.FindAsync(d => d.StoryVersionId == version.Id, cancellationToken: cancellationToken);
        var discussionValid = discussionItems.Count >= 2 &&
                              discussionItems.All(item => !string.IsNullOrWhiteSpace(item.Question)) &&
                              discussionItems.Any(item => item.IsMoralLesson);
        checks.Add(new ValidationCheckDto { Name = "discussion_valid", Passed = discussionValid, Message = $"{discussionItems.Count} questions" });
        if (!discussionValid) issues.Add($"Discussion needs at least 2 non-empty questions including the moral lesson (current: {discussionItems.Count})");

        var canApprove = storyValid && readability.Passed && vocabValid && quizPassed && discussionValid;
        return new ValidationResultDto
        {
            CanApprove = canApprove,
            Checks = checks,
            Issues = issues
        };
    }

    public async Task<ApproveResponseDto> ApproveAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        var storyForAuthorization = await _storyRepo.FirstOrDefaultAsync(
            s => s.Id == storyId, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("Story");
        await EnsureReviewPermissionAsync(storyForAuthorization, userId, cancellationToken);
        if (storyForAuthorization.Status != StoryStatus.ContentReview)
            throw new ConflictException("INVALID_STORY_STATUS");

        var validation = await ValidateAsync(userId, storyId, cancellationToken);
        if (!validation.CanApprove)
        {
            throw new BadRequestException($"Cannot approve: {string.Join(", ", validation.Issues)}");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, cancellationToken);
            var lockedValidation = await ValidateAsync(userId, storyId, cancellationToken);
            if (!lockedValidation.CanApprove)
                throw new BadRequestException($"Cannot approve: {string.Join(", ", lockedValidation.Issues)}");
            var story = await _storyRepo.FirstOrDefaultAsync(s => s.Id == storyId, cancellationToken: cancellationToken)
                ?? throw new NotFoundException("Story");
            await EnsureReviewPermissionAsync(story, userId, cancellationToken);
            if (story.Status != StoryStatus.ContentReview)
                throw new ConflictException("INVALID_STORY_STATUS");
            var approvedVersion = await _versionRepo.FirstOrDefaultAsync(
                v => v.StoryId == storyId && v.IsCurrent && v.Content != null,
                cancellationToken: cancellationToken)
                ?? throw new BadRequestException("Cannot approve without a current canonical story version.");

            var jobs = _unitOfWork.Repository<StoryGenerationJob>();
            if (await jobs.ExistsAsync(j =>
                    j.StoryId == storyId &&
                    j.Operation == GenerationJobOperation.GenerateMediaPackage,
                    cancellationToken))
                throw new ConflictException("MEDIA_JOB_ALREADY_EXISTS");

            var sourceJob = (await jobs.FindAsync(j =>
                    j.StoryId == storyId && j.StoryVersionId == approvedVersion.Id &&
                    j.GenerationRequestId != null,
                    cancellationToken: cancellationToken))
                .OrderByDescending(j => j.Id)
                .FirstOrDefault();
            await jobs.AddAsync(new StoryGenerationJob
            {
                StoryId = storyId,
                GenerationRequestId = sourceJob?.GenerationRequestId,
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

            story.Status = StoryStatus.Approved;
            _storyRepo.Update(story);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        return new ApproveResponseDto
        {
            Success = true,
            StoryId = storyId,
            Status = "Approved",
            ApprovedAt = DateTime.UtcNow
        };
    }

    public async Task<ArchiveResponseDto> ArchiveAsync(int userId, int storyId, ArchiveRequestDto input, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, cancellationToken);
            var story = await _storyRepo.FirstOrDefaultAsync(
                s => s.Id == storyId, cancellationToken: cancellationToken)
                ?? throw new NotFoundException("Story");
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

    private async Task EnsureReviewPermissionAsync(
        Story story, int userId, CancellationToken cancellationToken)
    {
        if (story.AuthorUserId == userId) return;
        var relationship = await _unitOfWork.Repository<SupervisionRelationship>().FirstOrDefaultAsync(
            value => value.ChildProfileId == story.ChildProfileId &&
                     value.SupervisorUserId == userId && value.RevokedAt == null,
            cancellationToken: cancellationToken);
        if (relationship is null) throw new ForbiddenException();
        if (relationship.SupervisorRole == SupervisorRole.Owner) return;
        var allowed = await _unitOfWork.Repository<SupervisionPermission>().ExistsAsync(
            value => value.SupervisionRelationshipId == relationship.Id &&
                     value.Permission == Permission.ApproveStory,
            cancellationToken);
        if (!allowed) throw new ForbiddenException("Bạn không có quyền duyệt hoặc lưu trữ Story này.");
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
