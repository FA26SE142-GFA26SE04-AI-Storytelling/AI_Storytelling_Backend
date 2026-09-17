using StoryPlatform.Application.Features.Auth.DTOs;
using StoryPlatform.Application.Features.Organizations.DTOs;

namespace StoryPlatform.Application.Features.Organizations.Interfaces;

public interface IOrganizationService
{
    Task<CreateOrganizationResponseDto> CreateOrganizationAsync(
        int adminUserId, CreateOrganizationRequestDto request, CancellationToken cancellationToken = default);

    Task<CreatedAccountDto> CreateTeacherAccountAsync(
        int organizationId, int creatorUserId, CreateTeacherAccountRequestDto request,
        CancellationToken cancellationToken = default);

    Task<List<OrganizationDto>> ListMyOrganizationsAsync(
        int currentUserId, CancellationToken cancellationToken = default);

    Task<OrganizationDto> GetOrganizationByIdAsync(
        int organizationId, int currentUserId, CancellationToken cancellationToken = default);
}
