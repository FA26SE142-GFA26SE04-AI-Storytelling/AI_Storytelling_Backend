namespace StoryPlatform.Application.Features.WishCards.Interfaces;
public interface IWishCardMatchService
{
    Task<IReadOnlyList<WishCardMatchDto>> ListAsync(int storyId, CancellationToken ct = default);
    Task<WishCardMatchDto> AddAsync(int actorUserId, int storyId, int wishCardId, CancellationToken ct = default);
    Task RemoveAsync(int actorUserId, int storyId, int wishCardId, CancellationToken ct = default);
}
