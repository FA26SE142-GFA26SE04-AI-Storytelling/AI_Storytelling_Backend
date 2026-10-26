using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.StoryWishes.Interfaces;

public interface IStoryWishNotifier
{
    Task NotifySubmittedAsync(StoryWish wish, CancellationToken ct = default);

    /// <summary>Nhắc ngày 6 tới Owner và người có ApproveStory. Payload: { childProfileId, wishId, kind, expiresAt }.</summary>
    Task NotifyExpiringAsync(StoryWish wish, CancellationToken ct = default);
}
