namespace StoryPlatform.Application.Features.StoryWishes.Interfaces;

public sealed record StoryWishSweepResult(int DraftsDeleted, int Cancelled, int Warned, int Expired, int Fulfilled, int Declined);

public interface IStoryWishSweepService
{
    Task<StoryWishSweepResult> SweepAsync(CancellationToken ct = default);
}
