using StoryPlatform.Application.Features.ChildProfiles.Safety.DTOs;

namespace StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;

public interface ISafetyPolicyService
{
    Task<SafetyPolicyDto> SetSafetyPolicyAsync(
        int childProfileId, int currentUserId, SetSafetyPolicyRequestDto request,
        CancellationToken cancellationToken = default);

    Task<SafetyPolicyDto> GetSafetyPolicyAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);

    Task<SafetyPolicyDto> ReconsentAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);

    Task<AgePresetDto> GetAgePresetAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);

    Task<SafetyPolicyDto> ApplyAgePresetAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);

    Task DeleteSafetyPolicyAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);
}
