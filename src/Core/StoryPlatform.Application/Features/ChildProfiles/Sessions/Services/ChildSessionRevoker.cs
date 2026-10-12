using System.Linq.Expressions;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ChildProfiles.Sessions.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ChildProfiles.Sessions.Services;

public class ChildSessionRevoker : IChildSessionRevoker
{
    private readonly IUnitOfWork _unitOfWork;

    public ChildSessionRevoker(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<int> RevokeByChildProfileAsync(
        int childProfileId, CancellationToken cancellationToken = default) =>
        RevokeAsync(session => session.ChildProfileId == childProfileId, cancellationToken);

    public Task<int> RevokeBySupervisorAsync(
        int supervisorUserId, int? childProfileId, CancellationToken cancellationToken = default) =>
        RevokeAsync(
            session => session.SupervisorSession != null
                       && session.SupervisorSession.UserAccountId == supervisorUserId
                       && (childProfileId == null || session.ChildProfileId == childProfileId),
            cancellationToken);

    private async Task<int> RevokeAsync(
        Expression<Func<ChildSession, bool>> predicate, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.Repository<ChildSession>();
        var sessions = await repository.FindAsync(predicate, cancellationToken: cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var session in sessions)
        {
            session.IsDeleted = true;
            session.UpdatedAt = now;
            repository.Update(session);
        }

        return sessions.Count;
    }
}
