using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;

public interface IChildRoadmapNotifier
{
    Task NotifyOtherSupervisorsAsync(
        int childProfileId, int actorUserId, NotificationType type, object payload,
        CancellationToken cancellationToken = default);

    Task NotifyRoadmapCompletedAsync(int childProfileId, CancellationToken cancellationToken = default);
}
