using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Application.Features.StoryReview.Interfaces;

namespace StoryPlatform.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/stories/{storyId:int}/review")]
public sealed class ReviewController : ControllerBase
{
    private readonly IStoryReviewService _reviewService;
    private readonly IProposalService _proposalService;

    public ReviewController(
        IStoryReviewService reviewService,
        IProposalService proposalService)
    {
        _reviewService = reviewService;
        _proposalService = proposalService;
    }

    private int CurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(value, out var userId) ? userId : throw new UnauthorizedAccessException();
    }

    #region Package

    [HttpGet]
    public async Task<ActionResult<ApiResponse<ReviewPackageDto>>> GetReviewPackage(
        int storyId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.GetReviewPackageAsync(CurrentUserId(), storyId, cancellationToken);
        return Ok(ApiResponse<ReviewPackageDto>.Ok(result));
    }

    #endregion

    #region Story Review

    [HttpGet("story")]
    public async Task<ActionResult<ApiResponse<StoryReviewDto>>> GetStory(
        int storyId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.GetStoryForReviewAsync(CurrentUserId(), storyId, cancellationToken);
        return Ok(ApiResponse<StoryReviewDto>.Ok(result));
    }

    [HttpPut("story")]
    public async Task<ActionResult<ApiResponse<StoryReviewDto>>> UpdateStory(
        int storyId, [FromBody] UpdateStoryRequestDto input, CancellationToken cancellationToken)
    {
        var result = await _reviewService.UpdateStoryAsync(CurrentUserId(), storyId, input, cancellationToken);
        return Ok(ApiResponse<StoryReviewDto>.Ok(result, "Story đã được cập nhật."));
    }

    [HttpPost("story/ai/partial-edit")]
    public async Task<ActionResult<ApiResponse<CreateProposalResponseDto>>> PartialEdit(
        int storyId, [FromBody] PartialEditRequestDto input, CancellationToken cancellationToken)
    {
        var result = await _reviewService.CreateProposalAsync(CurrentUserId(), storyId, input, cancellationToken);
        return Accepted(ApiResponse<CreateProposalResponseDto>.Ok(result, "Đã tạo đề xuất AI để xem trước."));
    }

    [HttpPost("story/complete")]
    public async Task<ActionResult<ApiResponse<bool>>> CompleteStory(
        int storyId, CancellationToken cancellationToken)
    {
        // Mark story review as complete - just validate it's done
        var story = await _reviewService.GetStoryForReviewAsync(CurrentUserId(), storyId, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, "Story review đã hoàn tất."));
    }

    #endregion

    #region Vocabulary Review

    [HttpGet("vocabulary")]
    public async Task<ActionResult<ApiResponse<VocabularyReviewDto>>> GetVocabulary(
        int storyId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.GetVocabularyForReviewAsync(CurrentUserId(), storyId, cancellationToken);
        return Ok(ApiResponse<VocabularyReviewDto>.Ok(result));
    }

    [HttpPut("vocabulary")]
    public async Task<ActionResult<ApiResponse<VocabularyReviewDto>>> UpdateVocabulary(
        int storyId, [FromBody] UpdateVocabularyRequestDto input, CancellationToken cancellationToken)
    {
        var result = await _reviewService.UpdateVocabularyAsync(CurrentUserId(), storyId, input, cancellationToken);
        return Ok(ApiResponse<VocabularyReviewDto>.Ok(result, "Vocabulary đã được cập nhật."));
    }

    [HttpPost("vocabulary/ai/regenerate")]
    public async Task<ActionResult<ApiResponse<CreateProposalResponseDto>>> RegenerateVocabulary(
        int storyId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.CreateVocabularyProposalAsync(CurrentUserId(), storyId, cancellationToken);
        return Accepted(ApiResponse<CreateProposalResponseDto>.Ok(result, "Đã tạo đề xuất vocabulary để xem trước."));
    }

    [HttpPost("vocabulary/complete")]
    public async Task<ActionResult<ApiResponse<bool>>> CompleteVocabulary(
        int storyId, CancellationToken cancellationToken)
    {
        var vocab = await _reviewService.GetVocabularyForReviewAsync(CurrentUserId(), storyId, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, "Vocabulary review đã hoàn tất."));
    }

    #endregion

    #region Quiz Review

    [HttpGet("quiz")]
    public async Task<ActionResult<ApiResponse<QuizReviewDto>>> GetQuiz(
        int storyId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.GetQuizForReviewAsync(CurrentUserId(), storyId, cancellationToken);
        return Ok(ApiResponse<QuizReviewDto>.Ok(result));
    }

    [HttpPut("quiz")]
    public async Task<ActionResult<ApiResponse<QuizReviewDto>>> UpdateQuiz(
        int storyId, [FromBody] UpdateQuizRequestDto input, CancellationToken cancellationToken)
    {
        var result = await _reviewService.UpdateQuizAsync(CurrentUserId(), storyId, input, cancellationToken);
        return Ok(ApiResponse<QuizReviewDto>.Ok(result, "Quiz đã được cập nhật."));
    }

    [HttpPost("quiz/ai/regenerate")]
    public async Task<ActionResult<ApiResponse<CreateProposalResponseDto>>> RegenerateQuiz(
        int storyId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.CreateQuizProposalAsync(CurrentUserId(), storyId, cancellationToken);
        return Accepted(ApiResponse<CreateProposalResponseDto>.Ok(result, "Đã tạo đề xuất quiz để xem trước."));
    }

    [HttpPost("quiz/complete")]
    public async Task<ActionResult<ApiResponse<bool>>> CompleteQuiz(
        int storyId, CancellationToken cancellationToken)
    {
        var quiz = await _reviewService.GetQuizForReviewAsync(CurrentUserId(), storyId, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, "Quiz review đã hoàn tất."));
    }

    #endregion

    #region Discussion Review

    [HttpGet("discussion")]
    public async Task<ActionResult<ApiResponse<DiscussionReviewDto>>> GetDiscussion(
        int storyId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.GetDiscussionForReviewAsync(CurrentUserId(), storyId, cancellationToken);
        return Ok(ApiResponse<DiscussionReviewDto>.Ok(result));
    }

    [HttpPut("discussion")]
    public async Task<ActionResult<ApiResponse<DiscussionReviewDto>>> UpdateDiscussion(
        int storyId, [FromBody] UpdateDiscussionRequestDto input, CancellationToken cancellationToken)
    {
        var result = await _reviewService.UpdateDiscussionAsync(CurrentUserId(), storyId, input, cancellationToken);
        return Ok(ApiResponse<DiscussionReviewDto>.Ok(result, "Discussion đã được cập nhật."));
    }

    [HttpPost("discussion/ai/regenerate")]
    public async Task<ActionResult<ApiResponse<CreateProposalResponseDto>>> RegenerateDiscussion(
        int storyId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.CreateDiscussionProposalAsync(CurrentUserId(), storyId, cancellationToken);
        return Accepted(ApiResponse<CreateProposalResponseDto>.Ok(result, "Đã tạo đề xuất discussion để xem trước."));
    }

    [HttpPost("discussion/complete")]
    public async Task<ActionResult<ApiResponse<bool>>> CompleteDiscussion(
        int storyId, CancellationToken cancellationToken)
    {
        var discussion = await _reviewService.GetDiscussionForReviewAsync(CurrentUserId(), storyId, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, "Discussion review đã hoàn tất."));
    }

    #endregion

    #region Validation & Approval

    [HttpGet("validation")]
    public async Task<ActionResult<ApiResponse<ValidationResultDto>>> Validate(
        int storyId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.ValidateAsync(CurrentUserId(), storyId, cancellationToken);
        return Ok(ApiResponse<ValidationResultDto>.Ok(result));
    }

    [HttpPost("approve")]
    [Authorize(Roles = "Parent,Teacher,Administrator")]
    public async Task<ActionResult<ApiResponse<ApproveResponseDto>>> Approve(
        int storyId, CancellationToken cancellationToken)
    {
        var result = await _reviewService.ApproveAsync(CurrentUserId(), storyId, cancellationToken);
        return Ok(ApiResponse<ApproveResponseDto>.Ok(result, "Story đã được phê duyệt."));
    }

    [HttpPost("archive")]
    [Authorize(Roles = "Parent,Teacher,Administrator")]
    public async Task<ActionResult<ApiResponse<ArchiveResponseDto>>> Archive(
        int storyId, [FromBody] ArchiveRequestDto input, CancellationToken cancellationToken)
    {
        var result = await _reviewService.ArchiveAsync(CurrentUserId(), storyId, input, cancellationToken);
        return Ok(ApiResponse<ArchiveResponseDto>.Ok(result, "Story đã được lưu trữ."));
    }

    #endregion

    #region Proposal Management

    [HttpGet("proposals/{proposalId}")]
    public async Task<ActionResult<ApiResponse<AIProposalDto>>> GetProposal(
        int storyId, string proposalId, CancellationToken cancellationToken)
    {
        var result = await _proposalService.GetProposalAsync(CurrentUserId(), storyId, proposalId, cancellationToken);
        if (result == null)
        {
            return NotFound(ApiResponse<AIProposalDto>.Fail("Proposal không tìm thấy hoặc đã hết hạn."));
        }
        return Ok(ApiResponse<AIProposalDto>.Ok(result));
    }

    [HttpPost("proposals/{proposalId}/apply")]
    public async Task<ActionResult<ApiResponse<ApplyDiscardResponseDto>>> ApplyProposal(
        int storyId, string proposalId, CancellationToken cancellationToken)
    {
        var result = await _proposalService.ApplyProposalAsync(CurrentUserId(), storyId, proposalId, cancellationToken);
        return Ok(ApiResponse<ApplyDiscardResponseDto>.Ok(result, result.Message));
    }

    [HttpPost("proposals/{proposalId}/discard")]
    public async Task<ActionResult<ApiResponse<ApplyDiscardResponseDto>>> DiscardProposal(
        int storyId, string proposalId, CancellationToken cancellationToken)
    {
        var result = await _proposalService.DiscardProposalAsync(CurrentUserId(), storyId, proposalId, cancellationToken);
        return Ok(ApiResponse<ApplyDiscardResponseDto>.Ok(result, result.Message));
    }

    #endregion
}
