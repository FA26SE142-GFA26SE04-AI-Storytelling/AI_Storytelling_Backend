using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.DTOs;

namespace StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.Interfaces;

public interface IChildAccessCredentialService
{
    Task SetPinAsync(
        int childProfileId, int currentUserId, SetChildAccessCredentialRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ChildSessionDto> LoginWithPinAsync(
        int childProfileId, string pin,
        CancellationToken cancellationToken = default);

    Task<ChildAccessCredentialDto> GetCredentialAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);

    Task RevokeCredentialAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);

    Task<ChildSessionProfileDto> GetMySessionProfileAsync(
        int childProfileId, CancellationToken cancellationToken = default);
}
