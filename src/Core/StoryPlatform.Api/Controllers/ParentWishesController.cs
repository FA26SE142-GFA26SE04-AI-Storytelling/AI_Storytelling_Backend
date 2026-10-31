using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AIStoryInput.DTOs;
using StoryPlatform.Application.Features.StoryWishes;
using StoryPlatform.Application.Features.StoryWishes.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Phụ huynh xử lý điều ước của con (WF-L2D UC-L2D-04). Quyền ApproveStory được kiểm trong service.</summary>
[Authorize(Roles = "User")]
[Route("api/v1/parent/wishes")]
public sealed class ParentWishesController(IParentStoryWishService service) : BaseApiController
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ParentWishDto>>>> List(
        [FromQuery] int childProfileId, CancellationToken ct)
    {
        var wishes = await service.ListAsync(GetCurrentUserId(), childProfileId, ct);
        return Ok(ApiResponse<IReadOnlyList<ParentWishDto>>.Ok(wishes));
    }

    [HttpGet("{wishId:int}")]
    public async Task<ActionResult<ApiResponse<ParentWishDetailDto>>> Get(int wishId, CancellationToken ct)
    {
        var detail = await service.GetAsync(GetCurrentUserId(), wishId, ct);
        return Ok(ApiResponse<ParentWishDetailDto>.Ok(detail));
    }

    [HttpGet("{wishId:int}/generation-form")]
    public async Task<ActionResult<ApiResponse<ParentWishGenerationFormDto>>> GetGenerationForm(
        int wishId, CancellationToken ct)
    {
        var form = await service.GetGenerationFormAsync(GetCurrentUserId(), wishId, ct);
        return Ok(ApiResponse<ParentWishGenerationFormDto>.Ok(form));
    }

    /// <summary>Tạo truyện mới: gửi lại form (đã chỉnh) vào luồng sinh truyện; trừ 1 lượt.</summary>
    [HttpPost("{wishId:int}/create-story")]
    public async Task<ActionResult<ApiResponse<ParentWishCreateStoryResultDto>>> CreateStory(
        int wishId, [FromBody] SubmitAIStoryInputRequestDto request, CancellationToken ct)
    {
        var result = await service.CreateStoryAsync(GetCurrentUserId(), wishId, request, ct);
        return Ok(ApiResponse<ParentWishCreateStoryResultDto>.Ok(result, "Đã gửi yêu cầu sinh truyện cho điều ước."));
    }

    [HttpGet("{wishId:int}/generation")]
    public async Task<ActionResult<ApiResponse<AIStoryInputProgressDto>>> GetGenerationProgress(int wishId, CancellationToken ct) =>
        Ok(ApiResponse<AIStoryInputProgressDto>.Ok(await service.GetGenerationProgressAsync(GetCurrentUserId(), wishId, ct)));

    /// <summary>Dùng truyện có sẵn trong danh sách gợi ý; không trừ lượt.</summary>
    [HttpPost("{wishId:int}/use-existing")]
    public async Task<ActionResult<ApiResponse<ParentWishDto>>> UseExisting(
        int wishId, [FromBody] UseExistingStoryRequestDto request, CancellationToken ct)
    {
        var wish = await service.UseExistingAsync(GetCurrentUserId(), wishId, request, ct);
        return Ok(ApiResponse<ParentWishDto>.Ok(wish, "Đã dùng truyện có sẵn cho điều ước."));
    }

    [HttpPost("{wishId:int}/decline")]
    public async Task<ActionResult<ApiResponse<ParentWishDto>>> Decline(
        int wishId, [FromBody] DeclineWishRequestDto request, CancellationToken ct)
    {
        var wish = await service.DeclineAsync(GetCurrentUserId(), wishId, request, ct);
        return Ok(ApiResponse<ParentWishDto>.Ok(wish, "Đã từ chối điều ước."));
    }
}
