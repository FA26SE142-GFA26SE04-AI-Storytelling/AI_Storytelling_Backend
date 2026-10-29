using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;

public interface IChildProfileEraser
{
    Task EraseAsync(ChildProfile child, int actorUserId, DateTime now, CancellationToken cancellationToken = default);
}
