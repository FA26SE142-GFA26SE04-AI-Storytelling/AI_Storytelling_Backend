using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Common;

public static class ChildOwnership
{
    public static Task<bool> IsOwnerAsync(
        IUnitOfWork unitOfWork, int? childProfileId, int userId, CancellationToken cancellationToken) =>
        childProfileId is int id
            ? unitOfWork.Repository<ChildProfile>().ExistsAsync(
                child => child.Id == id
                         && child.OwnerUserId == userId
                         && !child.IsDeleted
                         && child.Status != ChildProfileStatus.Deleted,
                cancellationToken)
            : Task.FromResult(false);
}
