using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Supervision.Services;

public class SupervisionAccessGuard : ISupervisionAccessGuard
{
    private readonly IUnitOfWork _unitOfWork;

    public SupervisionAccessGuard(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<SupervisionRelationship> EnsureActiveSupervisionAsync(
        int childProfileId, int userId, CancellationToken cancellationToken = default)
    {
        var relationship = await _unitOfWork.Repository<SupervisionRelationship>()
            .FirstOrDefaultAsync(
                value => value.ChildProfileId == childProfileId
                         && value.SupervisorUserId == userId
                         && value.RevokedAt == null,
                cancellationToken: cancellationToken);

        if (relationship == null)
        {
            throw new ForbiddenException("Bạn không có quyền giám sát hồ sơ trẻ này.");
        }

        return relationship;
    }

    public async Task EnsureOwnerAsync(
        int childProfileId, int userId, CancellationToken cancellationToken = default)
    {
        var relationship = await EnsureActiveSupervisionAsync(
            childProfileId, userId, cancellationToken);
        if (relationship.SupervisorRole != SupervisorRole.Owner)
        {
            throw new ForbiddenException("Chỉ Owner mới có quyền thực hiện thao tác này.");
        }
    }

    public async Task EnsurePermissionAsync(
        int childProfileId, int userId, Permission permission,
        CancellationToken cancellationToken = default)
    {
        var relationship = await EnsureActiveSupervisionAsync(
            childProfileId, userId, cancellationToken);
        if (relationship.SupervisorRole == SupervisorRole.Owner)
        {
            return;
        }

        var hasPermission = await _unitOfWork.Repository<SupervisionPermission>()
            .ExistsAsync(
                value => value.SupervisionRelationshipId == relationship.Id
                         && value.Permission == permission,
                cancellationToken);

        if (!hasPermission)
        {
            throw new ForbiddenException(
                $"Bạn không có quyền '{permission}' đối với hồ sơ trẻ này.");
        }
    }
}
