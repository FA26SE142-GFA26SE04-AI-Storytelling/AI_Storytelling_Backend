using StoryPlatform.Application.Features.ReadingSessions;

namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IReadingSessionInterruptService
{
    Task<int> RequestStoryInterruptAsync(
        int storyId, CancellationToken cancellationToken = default);

    Task<ForceExitSignalDto> CheckAsync(
        int childProfileId, int sessionId, CancellationToken cancellationToken = default);
}
