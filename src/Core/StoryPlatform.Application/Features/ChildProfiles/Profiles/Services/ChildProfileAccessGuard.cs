using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;

public class ChildProfileAccessGuard : IChildProfileAccessGuard
{
    private readonly IUnitOfWork _unitOfWork;

    public ChildProfileAccessGuard(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<ChildProfile> EnsureOwnerAsync(int childProfileId, int userId, CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken);
        if (profile == null || profile.IsDeleted || profile.Status == ChildProfileStatus.Deleted || profile.OwnerUserId != userId)
            throw new ForbiddenException("Bạn không có quyền với hồ sơ trẻ này.");

        return profile;
    }

    public Task<bool> IsOwnerAsync(int childProfileId, int userId, CancellationToken cancellationToken = default) =>
        ChildOwnership.IsOwnerAsync(_unitOfWork, childProfileId, userId, cancellationToken);
}
