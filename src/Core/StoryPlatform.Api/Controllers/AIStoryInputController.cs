using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AIStoryInput.DTOs;
using StoryPlatform.Application.Features.AIStoryInput.Interfaces;

namespace StoryPlatform.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/ai-story-input")]
public sealed class AIStoryInputController : ControllerBase
{
    private readonly IAIStoryInputService _service;

    public AIStoryInputController(IAIStoryInputService service)
    {
        _service = service;
    }

    [HttpGet("children/{childProfileId:int}/context")]
    public async Task<ActionResult<ApiResponse<AIStoryInputContextDto>>> GetContext(
        int childProfileId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetContextAsync(GetCurrentUserId(), childProfileId, cancellationToken);
        return Ok(ApiResponse<AIStoryInputContextDto>.Ok(result, "Tải context tạo truyện thành công."));
    }

    [HttpPost("requests")]
    public async Task<ActionResult<ApiResponse<AIStoryInputProgressDto>>> Submit(
        [FromBody] SubmitAIStoryInputRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _service.SubmitAsync(GetCurrentUserId(), request, cancellationToken);
        return ToInputResult(result);
    }

    [HttpGet("stories/{storyId:int}/requests/{requestId:int}")]
    public async Task<ActionResult<ApiResponse<AIStoryInputProgressDto>>> GetProgress(
        int storyId,
        int requestId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetProgressAsync(GetCurrentUserId(), storyId, requestId, cancellationToken);
        return Ok(ApiResponse<AIStoryInputProgressDto>.Ok(result, "Lấy trạng thái kiểm tra input thành công."));
    }

    [HttpPost("stories/{storyId:int}/requests/{requestId:int}/retry")]
    public async Task<ActionResult<ApiResponse<AIStoryInputProgressDto>>> Retry(
        int storyId,
        int requestId,
        [FromBody] RetryAIStoryInputRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _service.RetryAsync(GetCurrentUserId(), storyId, requestId, request, cancellationToken);
        return ToInputResult(result);
    }

    private ActionResult<ApiResponse<AIStoryInputProgressDto>> ToInputResult(AIStoryInputProgressDto result)
    {
        var message = result.InputStatus switch
        {
            "input_accepted" => "Input đã được chấp nhận và đang chờ bàn giao Generate Outline.",
            "input_blocked" => result.FallbackMessage ?? "Input bị chặn bởi Safety Policy.",
            "input_check_failed" => result.FallbackMessage ?? "Không thể hoàn tất kiểm tra input.",
            _ => "Input đang được kiểm tra."
        };
        var response = result.InputStatus is "input_accepted" or "checking_input"
            ? ApiResponse<AIStoryInputProgressDto>.Ok(result, message)
            : new ApiResponse<AIStoryInputProgressDto>
            {
                Success = false,
                Message = message,
                Data = result,
                Errors = result.ReasonCode is null ? null : [result.ReasonCode]
            };

        return result.InputStatus switch
        {
            "input_accepted" or "checking_input" => StatusCode(StatusCodes.Status202Accepted, response),
            "input_blocked" => StatusCode(StatusCodes.Status422UnprocessableEntity, response),
            "input_check_failed" when result.ReasonCode == "RESTRICTED_CONTENT_REQUIRES_REVIEW" =>
                StatusCode(StatusCodes.Status422UnprocessableEntity, response),
            "input_check_failed" when result.ReasonCode is "INPUT_CONTEXT_CHANGED" or "AUTHORIZATION_OR_CHILD_CHANGED" =>
                StatusCode(StatusCodes.Status409Conflict, response),
            "input_check_failed" => StatusCode(StatusCodes.Status503ServiceUnavailable, response),
            _ => Ok(response)
        };
    }

    private int GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(value, out var userId))
        {
            throw new UnauthorizedAccessException();
        }

        return userId;
    }
}
