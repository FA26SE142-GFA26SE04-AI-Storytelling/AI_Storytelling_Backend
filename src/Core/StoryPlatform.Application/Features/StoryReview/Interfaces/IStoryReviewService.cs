using StoryPlatform.Application.Features.StoryReview.DTOs;

using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryReview.Interfaces;

public interface IStoryReviewService
{
    #region Package

    Task<ReviewPackageDto> GetReviewPackageAsync(int userId, int storyId, CancellationToken cancellationToken = default);

    #endregion

    #region Story Review

    Task<StoryReviewDto> GetStoryForReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<StoryReviewDto> UpdateStoryAsync(int userId, int storyId, UpdateStoryRequestDto input, CancellationToken cancellationToken = default);
    Task<CreateProposalResponseDto> CreateProposalAsync(int userId, int storyId, PartialEditRequestDto input, CancellationToken cancellationToken = default);

    #endregion

    #region Vocabulary Review

    Task<VocabularyReviewDto> GetVocabularyForReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<VocabularyReviewDto> UpdateVocabularyAsync(int userId, int storyId, UpdateVocabularyRequestDto input, CancellationToken cancellationToken = default);
    Task<CreateProposalResponseDto> CreateVocabularyProposalAsync(int userId, int storyId, CancellationToken cancellationToken = default);

    #endregion

    #region Quiz Review

    Task<QuizReviewDto> GetQuizForReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<QuizReviewDto> UpdateQuizAsync(int userId, int storyId, UpdateQuizRequestDto input, CancellationToken cancellationToken = default);
    Task<CreateProposalResponseDto> CreateQuizProposalAsync(int userId, int storyId, CancellationToken cancellationToken = default);

    #endregion

    #region Validation & Approval

    Task<ListeningReviewDto> GetListeningForReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<ListeningReviewDto> UpdateListeningInteractionAsync(int userId, int storyId, int interactionId, UpdateListeningInteractionRequestDto input, CancellationToken cancellationToken = default);
    Task<bool> CompleteListeningReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<CreateProposalResponseDto> CreateListeningProposalAsync(int userId, int storyId, ListeningInteractionType type, CancellationToken cancellationToken = default);
    Task ApplyListeningProposalAsync(int userId, AIProposalDto proposal, CancellationToken cancellationToken = default);

    Task<bool> CompleteStoryReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<bool> CompleteVocabularyReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<bool> CompleteQuizReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default);

    Task<ValidationResultDto> ValidateAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<ConfirmCreatorContentResponseDto> ConfirmCreatorContentAsync(int userId, int storyId, ConfirmCreatorContentRequestDto input, CancellationToken cancellationToken = default);
    Task<ApproveResponseDto> ApproveAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<ApproveResponseDto?> EvaluateAndApplyAutoPublishAsync(int storyId, CancellationToken cancellationToken = default);
    Task<RejectStoryResponseDto> RejectAsync(int userId, int storyId, RejectStoryRequestDto input, CancellationToken cancellationToken = default);

    #endregion
}

public interface IProposalService
{
    Task<AIProposalDto?> GetProposalAsync(int userId, int storyId, string proposalId, CancellationToken cancellationToken = default);
    Task<ApplyDiscardResponseDto> ApplyProposalAsync(int userId, int storyId, string proposalId, CancellationToken cancellationToken = default);
    Task<ApplyDiscardResponseDto> DiscardProposalAsync(int userId, int storyId, string proposalId, CancellationToken cancellationToken = default);
}
