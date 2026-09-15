using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Auth.DTOs;
using StoryPlatform.Application.Features.Auth.Interfaces;
using StoryPlatform.Application.Features.Organizations.DTOs;
using StoryPlatform.Application.Features.Organizations.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Organizations.Services;

public class OrganizationService : IOrganizationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserProvisioningService _userProvisioningService;
    private readonly IEmailSender _emailSender;

    public OrganizationService(
        IUnitOfWork unitOfWork, IUserProvisioningService userProvisioningService, IEmailSender emailSender)
    {
        _unitOfWork = unitOfWork;
        _userProvisioningService = userProvisioningService;
        _emailSender = emailSender;
    }

    public async Task<CreateOrganizationResponseDto> CreateOrganizationAsync(
        int adminUserId, CreateOrganizationRequestDto request, CancellationToken cancellationToken = default)
    {
        var admin = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(adminUserId, cancellationToken);
        if (admin == null)
        {
            throw new NotFoundException("Tài khoản", adminUserId);
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name) || name.Length > 150)
        {
            throw new BadRequestException("Tên tổ chức phải từ 1 đến 150 ký tự.");
        }

        var (schoolAdminAccount, rawSetPasswordToken) =
            await _userProvisioningService.CreatePendingAccountAsync(
                request.SchoolAdminUsername,
                request.SchoolAdminEmail,
                request.SchoolAdminFullName,
                request.SchoolAdminPhoneNumber,
                UserRole.Teacher,
                cancellationToken);

        var organization = new Organization
        {
            Name = name,
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            ContactEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim(),
            VerificationStatus = OrgVerification.Active,
            CreatedByUserId = adminUserId,
            VerifiedByAdminId = adminUserId
        };

        await _unitOfWork.Repository<Organization>().AddAsync(organization, cancellationToken);
        await _unitOfWork.Repository<OrganizationMembership>().AddAsync(
            new OrganizationMembership
            {
                Organization = organization,
                User = schoolAdminAccount,
                OrgRole = OrgRole.SchoolAdmin,
                Status = MembershipStatus.Active,
                InvitedByUserId = adminUserId
            },
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _emailSender.SendAccountProvisionedEmailAsync(
            schoolAdminAccount.Email,
            schoolAdminAccount.FullName,
            admin.FullName,
            "Quản trị tổ chức (SchoolAdmin)",
            rawSetPasswordToken,
            cancellationToken);

        return new CreateOrganizationResponseDto
        {
            Organization = MapOrganization(organization),
            SchoolAdminAccount = MapCreatedAccount(schoolAdminAccount)
        };
    }

    public async Task<CreatedAccountDto> CreateTeacherAccountAsync(
        int organizationId, int creatorUserId, CreateTeacherAccountRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var organization = await _unitOfWork.Repository<Organization>()
            .GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
        {
            throw new NotFoundException("Tổ chức", organizationId);
        }

        var creator = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(creatorUserId, cancellationToken);
        if (creator == null)
        {
            throw new NotFoundException("Tài khoản", creatorUserId);
        }

        var isSchoolAdmin = await _unitOfWork.Repository<OrganizationMembership>().ExistsAsync(
            membership => membership.OrganizationId == organizationId
                          && membership.UserId == creatorUserId
                          && membership.OrgRole == OrgRole.SchoolAdmin
                          && membership.Status == MembershipStatus.Active,
            cancellationToken);
        if (!isSchoolAdmin)
        {
            throw new ForbiddenException(
                "Chỉ quản trị tổ chức (SchoolAdmin) mới được tạo tài khoản giáo viên.");
        }

        var (teacherAccount, rawSetPasswordToken) =
            await _userProvisioningService.CreatePendingAccountAsync(
                request.Username,
                request.Email,
                request.FullName,
                request.PhoneNumber,
                UserRole.Teacher,
                cancellationToken);

        await _unitOfWork.Repository<OrganizationMembership>().AddAsync(
            new OrganizationMembership
            {
                OrganizationId = organizationId,
                User = teacherAccount,
                OrgRole = OrgRole.Teacher,
                Status = MembershipStatus.Active,
                InvitedByUserId = creatorUserId
            },
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _emailSender.SendAccountProvisionedEmailAsync(
            teacherAccount.Email,
            teacherAccount.FullName,
            creator.FullName,
            "Giáo viên (Teacher)",
            rawSetPasswordToken,
            cancellationToken);

        return MapCreatedAccount(teacherAccount);
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

    private static CreatedAccountDto MapCreatedAccount(UserAccount account) => new()
    {
        Id = account.Id,
        Username = account.Username,
        Email = account.Email,
        FullName = account.FullName,
        Role = account.Role.ToString(),
        Status = account.Status.ToString()
    };
}
