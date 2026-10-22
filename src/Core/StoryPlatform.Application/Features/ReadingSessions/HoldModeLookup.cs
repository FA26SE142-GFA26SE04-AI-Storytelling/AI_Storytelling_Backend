using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions;

public static class HoldModeLookup
{
    /// <summary>Ca Hold Mode chưa mở khóa của trẻ (nếu có).</summary>
    public static Task<InterventionCase?> FindOpenAsync(
        IUnitOfWork unitOfWork, int childProfileId, CancellationToken cancellationToken = default) =>
        unitOfWork.Repository<InterventionCase>().FirstOrDefaultAsync(
            holdCase => holdCase.ChildProfileId == childProfileId
                        && !holdCase.IsDeleted
                        && holdCase.Status != InterventionStatus.ResolvedUnlocked,
            cancellationToken: cancellationToken);
}
