using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.WishCards;
using StoryPlatform.Application.Features.WishCards.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>Thẻ ước cho trẻ chọn khi "Bé ước" (WF-L2D).</summary>
[ApiController]
[Authorize(Policy = ServiceExtensions.ChildSessionPolicy)]
[Route("api/v1/child/wish-cards")]
public sealed class ChildWishCardsController : ControllerBase
{
    private readonly IChildWishCardService _cards;

    public ChildWishCardsController(IChildWishCardService cards)
    {
        _cards = cards;
    }

    private int ChildId
    {
        get
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (!int.TryParse(claim, out var id))
            {
                throw new UnauthorizedAccessException();
            }

            return id;
        }
    }

    /// <summary>Tối đa 8 thẻ của một nhóm: Character, Place, Challenge hoặc Lesson.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ChildWishCardDto>>>> List(
        [FromQuery] string? group, CancellationToken ct)
    {
        var result = await _cards.ListAsync(ChildId, group, ct);
        return Ok(ApiResponse<IReadOnlyList<ChildWishCardDto>>.Ok(result));
    }
}
