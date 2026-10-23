using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.StoryWishes.Interfaces;

public interface IStoryWishNotifier
{
    Task NotifySubmittedAsync(StoryWish wish, CancellationToken ct = default);
}
