using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Sessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;

public class ChildProfileEraser : IChildProfileEraser
{
    public const string AnonymizedNickname = "[Đã ẩn danh]";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildSessionRevoker _childSessionRevoker;

    public ChildProfileEraser(IUnitOfWork unitOfWork, IChildSessionRevoker childSessionRevoker)
    {
        _unitOfWork = unitOfWork;
        _childSessionRevoker = childSessionRevoker;
    }

    public async Task EraseAsync(ChildProfile child, int actorUserId, DateTime now, CancellationToken cancellationToken = default)
    {
        child.Nickname = AnonymizedNickname;
        child.Status = ChildProfileStatus.Deleted;
        child.UpdatedAt = now;
        _unitOfWork.Repository<ChildProfile>().Update(child);

        await _childSessionRevoker.RevokeByChildProfileAsync(child.Id, cancellationToken);
        var credentialRepo = _unitOfWork.Repository<ChildAccessCredential>();
        var credential = await credentialRepo.FirstOrDefaultAsync(
            value => value.ChildProfileId == child.Id && value.RevokedAt == null,
            cancellationToken: cancellationToken);
        if (credential != null)
        {
            credential.RevokedAt = now;
            credential.EasyLoginSecretHash = null;
            credential.UpdatedAt = now;
            credentialRepo.Update(credential);
        }
    }
}
