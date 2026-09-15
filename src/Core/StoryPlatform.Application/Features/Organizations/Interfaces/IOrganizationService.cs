using StoryPlatform.Application.Features.Organizations.DTOs;

namespace StoryPlatform.Application.Features.Organizations.Interfaces;

public interface IOrganizationService
{
    Task<OrganizationDto> CreateOrganizationAsync(
        int creatorUserId, CreateOrganizationRequestDto request, CancellationToken cancellationToken = default);

    Task<List<OrganizationDto>> ListMyOrganizationsAsync(
        int currentUserId, CancellationToken cancellationToken = default);

    Task<OrganizationDto> GetOrganizationByIdAsync(
        int organizationId, int currentUserId, CancellationToken cancellationToken = default);
}
