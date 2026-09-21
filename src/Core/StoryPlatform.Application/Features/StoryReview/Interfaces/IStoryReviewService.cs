using StoryPlatform.Application.Features.StoryReview.DTOs;

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

    #region Discussion Review

    Task<DiscussionReviewDto> GetDiscussionForReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<DiscussionReviewDto> UpdateDiscussionAsync(int userId, int storyId, UpdateDiscussionRequestDto input, CancellationToken cancellationToken = default);
    Task<CreateProposalResponseDto> CreateDiscussionProposalAsync(int userId, int storyId, CancellationToken cancellationToken = default);

    #endregion

    #region Validation & Approval

    Task<bool> CompleteStoryReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<bool> CompleteVocabularyReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<bool> CompleteQuizReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<bool> CompleteDiscussionReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default);

    Task<ValidationResultDto> ValidateAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<ApproveResponseDto> ApproveAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<ApproveResponseDto?> EvaluateAndApplyAutoPublishAsync(int storyId, CancellationToken cancellationToken = default);
    Task<ArchiveResponseDto> ArchiveAsync(int userId, int storyId, ArchiveRequestDto input, CancellationToken cancellationToken = default);

    #endregion
}

public interface IProposalService
{
    Task<AIProposalDto?> GetProposalAsync(int userId, int storyId, string proposalId, CancellationToken cancellationToken = default);
    Task<ApplyDiscardResponseDto> ApplyProposalAsync(int userId, int storyId, string proposalId, CancellationToken cancellationToken = default);
    Task<ApplyDiscardResponseDto> DiscardProposalAsync(int userId, int storyId, string proposalId, CancellationToken cancellationToken = default);
}
