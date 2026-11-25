using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ChildProfiles.Safety.Services;

/// <summary>Đọc thẳng safety_policies mỗi lần (không cache) để phụ huynh vừa tắt là có hiệu lực ngay.</summary>
public class ChildFeatureService(IUnitOfWork uow) : IChildFeatureService
{
    public async Task<bool> IsEnabledAsync(int childProfileId, string code, CancellationToken cancellationToken = default) =>
        ChildFeatures.IsEnabled(await PolicyAsync(childProfileId, cancellationToken), code);

    public async Task<IReadOnlyList<string>> GetDisabledAsync(int childProfileId, CancellationToken cancellationToken = default)
    {
        var policy = await PolicyAsync(childProfileId, cancellationToken);
        return ChildFeatures.All.Where(f => !ChildFeatures.IsEnabled(policy, f.Code)).Select(f => f.Code).ToList();
    }

    private Task<SafetyPolicy?> PolicyAsync(int childProfileId, CancellationToken ct) =>
        uow.Repository<SafetyPolicy>().FirstOrDefaultAsync(p => p.ChildProfileId == childProfileId, cancellationToken: ct);
}
