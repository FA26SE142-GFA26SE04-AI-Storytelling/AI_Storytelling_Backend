using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.WishCards;
using StoryPlatform.Application.Features.WishCards.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Moderator gắn thẻ ước cho truyện (thay AI cho tới khi có AI gắn thẻ tự động).</summary>
[Authorize(Roles = "Moderator,Administrator")]
[Route("api/v1/moderation/stories/{storyId:int}/wish-cards")]
public sealed class WishCardMatchManagementController(IWishCardMatchService service) : BaseApiController
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WishCardMatchDto>>>> List(int storyId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<WishCardMatchDto>>.Ok(await service.ListAsync(storyId, ct)));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<WishCardMatchDto>>> Add(
        int storyId, [FromBody] AddWishCardMatchRequestDto request, CancellationToken ct) =>
        Ok(ApiResponse<WishCardMatchDto>.Ok(
            await service.AddAsync(GetCurrentUserId(), storyId, request.WishCardId, ct), "Đã gắn thẻ cho truyện."));

    [HttpDelete("{wishCardId:int}")]
    public async Task<ActionResult<ApiResponse<object?>>> Remove(int storyId, int wishCardId, CancellationToken ct)
    {
        await service.RemoveAsync(GetCurrentUserId(), storyId, wishCardId, ct);
        return Ok(ApiResponse<object?>.Ok(null, "Đã gỡ thẻ khỏi truyện."));
    }
}
