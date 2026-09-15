using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Organizations.DTOs;
using StoryPlatform.Application.Features.Organizations.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Organizations.Services;

public class OrganizationService : IOrganizationService
{
    private readonly IUnitOfWork _unitOfWork;

    public OrganizationService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<OrganizationDto> CreateOrganizationAsync(
        int creatorUserId, CreateOrganizationRequestDto request, CancellationToken cancellationToken = default)
    {
        var creator = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(creatorUserId, cancellationToken);
        if (creator == null)
        {
            throw new NotFoundException("Tài khoản", creatorUserId);
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name) || name.Length > 150)
        {
            throw new BadRequestException("Tên tổ chức phải từ 1 đến 150 ký tự.");
        }

        var organization = new Organization
        {
            Name = name,
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            ContactEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim(),
            VerificationStatus = OrgVerification.PendingVerification,
            CreatedByUserId = creatorUserId
        };

        await _unitOfWork.Repository<Organization>().AddAsync(organization, cancellationToken);
        await _unitOfWork.Repository<OrganizationMembership>().AddAsync(
            new OrganizationMembership
            {
                Organization = organization,
                UserId = creatorUserId,
                OrgRole = OrgRole.SchoolAdmin,
                Status = MembershipStatus.Active
            },
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapOrganization(organization);
    }

    public async Task<List<OrganizationDto>> ListMyOrganizationsAsync(
        int currentUserId, CancellationToken cancellationToken = default)
    {
        var memberships = await _unitOfWork.Repository<OrganizationMembership>().FindAsync(
            membership => membership.UserId == currentUserId
                          && membership.Status == MembershipStatus.Active,
            includeProperties: "Organization",
            cancellationToken: cancellationToken);

        return memberships
            .Where(membership => membership.Organization != null)
            .Select(membership => MapOrganization(membership.Organization!))
            .ToList();
    }

    public async Task<OrganizationDto> GetOrganizationByIdAsync(
        int organizationId, int currentUserId, CancellationToken cancellationToken = default)
    {
        var organization = await _unitOfWork.Repository<Organization>()
            .GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
        {
            throw new NotFoundException("Tổ chức", organizationId);
        }

        var isMember = await _unitOfWork.Repository<OrganizationMembership>().ExistsAsync(
            membership => membership.OrganizationId == organizationId
                          && membership.UserId == currentUserId
                          && membership.Status == MembershipStatus.Active,
            cancellationToken);
        if (!isMember)
        {
            throw new ForbiddenException("Bạn không có quyền truy cập tổ chức này.");
        }

        return MapOrganization(organization);
    }

    private static OrganizationDto MapOrganization(Organization organization) => new()
    {
        Id = organization.Id,
        Name = organization.Name,
        Address = organization.Address,
        ContactEmail = organization.ContactEmail,
        VerificationStatus = organization.VerificationStatus.ToString(),
        CreatedByUserId = organization.CreatedByUserId,
        CreatedAt = organization.CreatedAt
    };
}
