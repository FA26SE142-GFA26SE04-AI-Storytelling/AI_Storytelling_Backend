using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.DTOs;

namespace StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.Interfaces;

public interface IChildAccessCredentialService
{
    Task<ChildAccessCredentialDto> GetCredentialAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);

    Task<EasyLoginSecretDto> CreateOrRegenerateEasyLoginAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);

    Task RevokeCredentialAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);

    Task<ChildSessionDto> LoginWithEasyLoginAsync(
        string secret, string clientKey, CancellationToken cancellationToken = default);

    Task<ChildSessionProfileDto> GetMySessionProfileAsync(
        int childProfileId, CancellationToken cancellationToken = default);

    Task<ChildSessionDto> StartSupervisedSessionAsync(
        int childProfileId, int currentUserId, string supervisorRefreshToken,
        CancellationToken cancellationToken = default);
}
