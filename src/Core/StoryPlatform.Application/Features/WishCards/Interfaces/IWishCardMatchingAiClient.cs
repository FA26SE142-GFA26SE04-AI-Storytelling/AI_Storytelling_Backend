using StoryPlatform.Contracts.AI.WishCards;

namespace StoryPlatform.Application.Features.WishCards.Interfaces;

public interface IWishCardMatchingAiClient
{
    Task<MatchWishCardsResponse> MatchAsync(MatchWishCardsRequest request, CancellationToken ct);
}
