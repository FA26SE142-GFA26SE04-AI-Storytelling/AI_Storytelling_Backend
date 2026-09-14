using StoryPlatform.Application.Features.ChildProfiles.Safety.DTOs;

namespace StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;

public interface ISafetyPolicyService
{
    Task<SafetyPolicyDto> SetSafetyPolicyAsync(
        int childProfileId, int currentUserId, SetSafetyPolicyRequestDto request,
        CancellationToken cancellationToken = default);

    Task<SafetyPolicyDto> GetSafetyPolicyAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);

    Task DeleteSafetyPolicyAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);
}
