using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.WishCards;

namespace StoryPlatform.Application.Features.WishCards.Interfaces;

public interface IWishCardManagementService
{
    Task<PagedResult<WishCardDto>> ListAsync(WishCardQueryDto query, CancellationToken ct = default);
    Task<WishCardDto> GetAsync(int id, CancellationToken ct = default);
    Task<WishCardDto> CreateAsync(int actorUserId, CreateWishCardRequestDto request, CancellationToken ct = default);
    Task<WishCardDto> UpdateAsync(int actorUserId, int id, UpdateWishCardRequestDto request, CancellationToken ct = default);
    Task<WishCardDto> SetActiveAsync(int actorUserId, int id, bool isActive, CancellationToken ct = default);
}
