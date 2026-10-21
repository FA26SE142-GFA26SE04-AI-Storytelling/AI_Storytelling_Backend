using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;

public interface IChildRoadmapProgressRecorder
{
    Task<ChildRoadmapItem?> RecordSessionCompletedAsync(
        int readingSessionId, bool isRetake, CancellationToken cancellationToken = default);
}
