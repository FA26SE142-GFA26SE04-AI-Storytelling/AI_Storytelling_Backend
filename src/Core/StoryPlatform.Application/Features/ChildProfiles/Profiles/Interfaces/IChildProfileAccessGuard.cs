using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;

public interface IChildProfileAccessGuard
{
    Task<ChildProfile> EnsureOwnerAsync(int childProfileId, int userId, CancellationToken cancellationToken = default);
    Task<bool> IsOwnerAsync(int childProfileId, int userId, CancellationToken cancellationToken = default);
}
