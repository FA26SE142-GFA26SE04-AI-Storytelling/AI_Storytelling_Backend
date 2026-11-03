using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ContentGeneration.DTOs;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Application.Features.StoryReview.Interfaces;

namespace StoryPlatform.Api.Controllers;

[ApiController]
[Authorize(Roles = "User,ContentCreator,Administrator")]
[Route("api/v1/stories/{storyId:int}/review/confirm-package")]
public sealed class StoryPackageConfirmationController(IStoryReviewService review, IContentGenerationService generation) : ControllerBase
{
    /// <summary>Confirm the previewed story and all learning interactions together. Quality repair runs asynchronously.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(int storyId, ConfirmStoryPackageRequestDto input, CancellationToken ct)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(id, out var userId)) return Unauthorized();
        // Service checks ownership, the expected version, all quality gates, and atomically queues media.
        try
        {
            var response = await review.ConfirmPackageAsync(userId, storyId, input, ct);
            return Accepted(ApiResponse<ConfirmStoryPackageResponseDto>.Ok(response, "Đã xác nhận gói nội dung và xếp hàng media; chưa phát hành hoặc giao cho trẻ."));
        }
        catch (StoryPlatform.Application.Common.Exceptions.ConflictException ex) when (ex.Message == "CONTENT_READABILITY_REPAIR_REQUIRED")
        {
            var progress = await generation.RetryAsync(userId, storyId, new RetryContentGenerationRequestDto { RetryKey = input.OperationId }, ct);
            return Accepted(ApiResponse<ContentGenerationProgressDto>.Ok(progress, "AI đang tự hoàn thiện độ dễ đọc. Hãy xem trước phiên bản mới khi hoàn tất rồi xác nhận lại."));
        }
    }
}
