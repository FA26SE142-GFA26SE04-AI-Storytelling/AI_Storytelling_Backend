using StoryPlatform.Application.Features.WishCards;

namespace StoryPlatform.Application.Features.WishCards.Interfaces;

public interface IChildWishCardService
{
    Task<IReadOnlyList<ChildWishCardDto>> ListAsync(int childProfileId, string? group, CancellationToken ct = default);
}
