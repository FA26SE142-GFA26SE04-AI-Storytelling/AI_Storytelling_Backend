using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;

public interface ISupervisionAccessGuard
{
    Task<SupervisionRelationship> EnsureActiveSupervisionAsync(
        int childProfileId, int userId, CancellationToken cancellationToken = default);

    Task EnsureOwnerAsync(
        int childProfileId, int userId, CancellationToken cancellationToken = default);

    Task EnsurePermissionAsync(
        int childProfileId, int userId, Permission permission,
        CancellationToken cancellationToken = default);
}
