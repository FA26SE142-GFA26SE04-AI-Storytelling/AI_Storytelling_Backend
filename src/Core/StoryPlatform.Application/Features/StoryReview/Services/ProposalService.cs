using System.Text.Json;
using System.Linq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Application.Features.StoryReview.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryReview.Services;

public sealed class ProposalService : IProposalService
{
    private readonly IProposalCache _cache;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IGenericRepository<StoryVersion> _versionRepo;
    private readonly IGenericRepository<StoryVocabulary> _vocabRepo;
    private readonly IGenericRepository<QuizItem> _quizRepo;
    private readonly IGenericRepository<DiscussionQuestion> _discussionRepo;

    public ProposalService(
        IProposalCache cache,
        IUnitOfWork unitOfWork)
    {
        _cache = cache;
        _unitOfWork = unitOfWork;
        _versionRepo = unitOfWork.Repository<StoryVersion>();
        _vocabRepo = unitOfWork.Repository<StoryVocabulary>();
        _quizRepo = unitOfWork.Repository<QuizItem>();
        _discussionRepo = unitOfWork.Repository<DiscussionQuestion>();
    }

    public async Task<AIProposalDto?> GetProposalAsync(int userId, int storyId, string proposalId, CancellationToken cancellationToken = default)
    {
        var proposal = await _cache.GetAsync(proposalId);
        EnsureProposalAccess(proposal, userId, storyId);
        return proposal;
    }

    public async Task<ApplyDiscardResponseDto> ApplyProposalAsync(int userId, int storyId, string proposalId, CancellationToken cancellationToken = default)
    {
        var proposal = await _cache.GetAsync(proposalId);
        EnsureProposalAccess(proposal, userId, storyId);

        if (proposal!.Status != "pending")
        {
            throw new BadRequestException("Proposal is no longer pending");
        }

        switch (proposal.ArtifactType)
        {
            case "story" when proposal.OperationType == "partial_edit":
                await ApplyStoryProposalAsync(userId, proposal, cancellationToken);
                break;
            case "vocabulary":
                await ApplyVocabularyProposalAsync(proposal, cancellationToken);
                break;
            case "quiz":
                await ApplyQuizProposalAsync(proposal, cancellationToken);
                break;
            case "discussion":
                await ApplyDiscussionProposalAsync(proposal, cancellationToken);
                break;
            default:
                throw new BadRequestException($"Unknown artifact type: {proposal.ArtifactType}");
        }

        await _cache.UpdateStatusAsync(proposalId, "applied");

        return new ApplyDiscardResponseDto
        {
            Success = true,
            Message = "Proposal applied successfully"
        };
    }

    public async Task<ApplyDiscardResponseDto> DiscardProposalAsync(int userId, int storyId, string proposalId, CancellationToken cancellationToken = default)
    {
        var proposal = await _cache.GetAsync(proposalId);
        EnsureProposalAccess(proposal, userId, storyId);
        if (proposal!.Status != "pending")
        {
            throw new BadRequestException("Proposal is no longer pending");
        }

        await _cache.UpdateStatusAsync(proposalId, "discarded");

        return new ApplyDiscardResponseDto
        {
            Success = true,
            Message = "Proposal discarded"
        };
    }

    private async Task ApplyStoryProposalAsync(int userId, AIProposalDto proposal, CancellationToken cancellationToken)
    {
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.Id == proposal.StoryVersionId, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("StoryVersion");

        if (proposal.SuggestedContent is JsonElement element && element.TryGetProperty("content", out var contentProp))
        {
            var replacement = contentProp.GetString() ?? string.Empty;
            if (proposal.OriginalContent is JsonElement original &&
                original.TryGetProperty("start", out var startProperty) &&
                original.TryGetProperty("endExclusive", out var endProperty))
            {
                var currentContent = version.Content ?? string.Empty;
                var start = startProperty.GetInt32();
                var endExclusive = endProperty.GetInt32();
                if (start < 0 || endExclusive <= start || endExclusive > currentContent.Length)
                {
                    throw new BadRequestException("The story changed and the proposal selection is no longer valid.");
                }
                version.Content = string.Concat(currentContent.AsSpan(0, start), replacement, currentContent.AsSpan(endExclusive));
            }
            else
            {
                version.Content = replacement;
            }
            version.EditorUserId = userId;
            version.EditType = VersionEditType.AiRefined;
            _versionRepo.Update(version);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task ApplyVocabularyProposalAsync(AIProposalDto proposal, CancellationToken cancellationToken)
    {
        // Delete all existing vocabulary
        var existing = await _vocabRepo.FindAsync(v => v.StoryVersionId == proposal.StoryVersionId, cancellationToken: cancellationToken);
        foreach (var item in existing)
        {
            _vocabRepo.Delete(item);
        }

        // Add new vocabulary from proposal
        if (proposal.SuggestedContent is JsonElement element && element.TryGetProperty("items", out var itemsElement))
        {
            foreach (var item in itemsElement.EnumerateArray())
            {
                var term = item.GetProperty("term").GetString();
                var definition = item.GetProperty("definition").GetString();
                if (!string.IsNullOrEmpty(term) && !string.IsNullOrEmpty(definition))
                {
                    await _vocabRepo.AddAsync(new StoryVocabulary
                    {
                        StoryVersionId = proposal.StoryVersionId,
                        Term = term,
                        Definition = definition
                    }, cancellationToken);
                }
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task ApplyQuizProposalAsync(AIProposalDto proposal, CancellationToken cancellationToken)
    {
        // Delete all existing quiz items
        var existing = await _quizRepo.FindAsync(q => q.StoryVersionId == proposal.StoryVersionId, cancellationToken: cancellationToken);
        foreach (var item in existing)
        {
            _quizRepo.Delete(item);
        }

        // Add new quiz items from proposal
        if (proposal.SuggestedContent is JsonElement element && element.TryGetProperty("items", out var itemsElement))
        {
            foreach (var item in itemsElement.EnumerateArray())
            {
                var type = item.GetProperty("type").GetString();
                var question = item.GetProperty("question").GetString();
                var correctAnswer = item.TryGetProperty("correctAnswer", out var ca) ? ca.GetString() : null;
                var choices = item.TryGetProperty("choices", out var ch) && ch.ValueKind == JsonValueKind.Array
                    ? ch.EnumerateArray().Select(c => c.GetString()).Where(s => s != null).ToList()
                    : null;

                if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(question))
                {
                    await _quizRepo.AddAsync(new QuizItem
                    {
                        StoryVersionId = proposal.StoryVersionId,
                        Type = Enum.Parse<QuizType>(type, true),
                        Question = question,
                        CorrectAnswer = correctAnswer,
                        Choices = choices != null ? JsonSerializer.Serialize(choices) : null
                    }, cancellationToken);
                }
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task ApplyDiscussionProposalAsync(AIProposalDto proposal, CancellationToken cancellationToken)
    {
        // Delete all existing discussion questions
        var existing = await _discussionRepo.FindAsync(d => d.StoryVersionId == proposal.StoryVersionId, cancellationToken: cancellationToken);
        foreach (var item in existing)
        {
            _discussionRepo.Delete(item);
        }

        // Add new discussion questions from proposal
        if (proposal.SuggestedContent is JsonElement element && element.TryGetProperty("items", out var itemsElement))
        {
            foreach (var item in itemsElement.EnumerateArray())
            {
                var question = item.GetProperty("question").GetString();
                var isMoralLesson = item.TryGetProperty("isMoralLesson", out var iml) && iml.GetBoolean();

                if (!string.IsNullOrEmpty(question))
                {
                    await _discussionRepo.AddAsync(new DiscussionQuestion
                    {
                        StoryVersionId = proposal.StoryVersionId,
                        Question = question,
                        IsMoralLesson = isMoralLesson
                    }, cancellationToken);
                }
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private static void EnsureProposalAccess(AIProposalDto? proposal, int userId, int storyId)
    {
        if (proposal is null || proposal.StoryId != storyId || proposal.RequestedByUserId != userId)
        {
            throw new NotFoundException("Proposal");
        }
    }
}
