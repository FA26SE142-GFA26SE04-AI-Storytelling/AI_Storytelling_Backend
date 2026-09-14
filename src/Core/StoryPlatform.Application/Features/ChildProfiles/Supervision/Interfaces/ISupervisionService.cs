using StoryPlatform.Application.Features.ChildProfiles.Supervision.DTOs;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;

public interface ISupervisionService
{
    Task<InvitationDto> CreateInvitationAsync(
        int childProfileId, int inviterUserId, CreateInvitationRequestDto request,
        CancellationToken cancellationToken = default);

    Task<SupervisionRelationshipDto> AcceptInvitationAsync(
        string invitationCode, int accepterUserId,
        CancellationToken cancellationToken = default);

    Task RevokeSupervisionAsync(
        int supervisionRelationshipId, int revokerUserId,
        CancellationToken cancellationToken = default);

    Task GrantPermissionAsync(
        int supervisionRelationshipId, int ownerUserId, Permission permission,
        CancellationToken cancellationToken = default);

    Task RevokePermissionAsync(
        int supervisionRelationshipId, int ownerUserId, Permission permission,
        CancellationToken cancellationToken = default);

    Task<List<InvitationDto>> ListInvitationsAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);

    Task CancelInvitationAsync(
        int invitationId, int currentUserId, CancellationToken cancellationToken = default);

    Task<List<SupervisionRelationshipDto>> ListSupervisorsAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default);

    Task<List<string>> ListPermissionsAsync(
        int supervisionRelationshipId, int currentUserId, CancellationToken cancellationToken = default);
}
