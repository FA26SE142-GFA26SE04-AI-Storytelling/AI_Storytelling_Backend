using Microsoft.AspNetCore.Mvc;
using StoryPlatform.AI.Application.WishCards;
using StoryPlatform.Contracts.AI.WishCards;

namespace StoryPlatform.AI.Api.Controllers;

[ApiController]
[Route("api/ai/wish-cards")]
public sealed class WishCardMatchingController(MatchWishCardsHandler handler) : ControllerBase
{
    [HttpPost("match")]
    public Task<MatchWishCardsResponse> Match(MatchWishCardsRequest request, CancellationToken ct) =>
        handler.HandleAsync(request, ct);
}
