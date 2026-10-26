namespace StoryPlatform.Application.Features.StoryWishes.Interfaces;

public interface IStoryWishService
{
    Task<WishDraftDto> StartDraftAsync(int childProfileId, CancellationToken ct = default);
    Task<WishDraftDto> SelectCardAsync(
        int childProfileId, SelectWishCardRequestDto request, CancellationToken ct = default);
    Task<ChildWishDto> SubmitAsync(int childProfileId, CancellationToken ct = default);
    Task<IReadOnlyList<ChildWishDto>> ListMineAsync(int childProfileId, CancellationToken ct = default);
}
