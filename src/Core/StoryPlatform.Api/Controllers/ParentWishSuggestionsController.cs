using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.StoryWishes;
using StoryPlatform.Application.Features.StoryWishes.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Phụ huynh xem truyện gợi ý cho một điều ước của con (WF-L2D UC-L2D-04, phần danh sách gợi ý).</summary>
[Authorize(Roles = "User")]
[Route("api/v1/parent/wishes")]
public sealed class ParentWishSuggestionsController(IStoryWishSuggestionService service) : BaseApiController
{
    [HttpGet("{wishId:int}/suggestions")]
    public async Task<ActionResult<ApiResponse<ParentWishSuggestionsDto>>> Get(int wishId, CancellationToken ct) =>
        Ok(ApiResponse<ParentWishSuggestionsDto>.Ok(await service.ForParentAsync(GetCurrentUserId(), wishId, ct)));
}
