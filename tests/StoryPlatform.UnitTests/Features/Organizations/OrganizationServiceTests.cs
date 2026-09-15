using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Auth.Interfaces;
using StoryPlatform.Application.Features.Organizations.DTOs;
using StoryPlatform.Application.Features.Organizations.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Organizations;

public class OrganizationServiceTests
{
    private readonly Mock<IGenericRepository<Organization>> _organizationRepo = new();
    private readonly Mock<IGenericRepository<OrganizationMembership>> _membershipRepo = new();
    private readonly Mock<IGenericRepository<UserAccount>> _userRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IUserProvisioningService> _userProvisioningService = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly OrganizationService _sut;

    public OrganizationServiceTests()
    {
        _unitOfWork.Setup(work => work.Repository<Organization>()).Returns(_organizationRepo.Object);
        _unitOfWork.Setup(work => work.Repository<OrganizationMembership>()).Returns(_membershipRepo.Object);
        _unitOfWork.Setup(work => work.Repository<UserAccount>()).Returns(_userRepo.Object);
        _sut = new OrganizationService(
            _unitOfWork.Object, _userProvisioningService.Object, _emailSender.Object);
    }

    [Fact]
    public async Task CreateOrganizationAsync_ValidRequest_CreatesOrganizationAndSchoolAdminAccount()
    {
        _userRepo.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount
            {
                Id = 1,
                Role = UserRole.Administrator,
                FullName = "Admin Root"
            });
        var schoolAdminAccount = new UserAccount
        {
            Id = 2,
            Username = "schooladmin1",
            Email = "admin@schoolx.example",
            FullName = "Nguyen Van Hieu",
            Role = UserRole.Teacher,
            Status = AccountStatus.PasswordResetPending
        };
        _userProvisioningService.Setup(service => service.CreatePendingAccountAsync(
                "schooladmin1", "admin@schoolx.example", "Nguyen Van Hieu", null,
                UserRole.Teacher, It.IsAny<CancellationToken>()))
            .ReturnsAsync((schoolAdminAccount, "raw-set-password-token"));

        var result = await _sut.CreateOrganizationAsync(
            1,
            new CreateOrganizationRequestDto
            {
                Name = "  Trường Tiểu Học ABC  ",
                SchoolAdminUsername = "schooladmin1",
                SchoolAdminEmail = "admin@schoolx.example",
                SchoolAdminFullName = "Nguyen Van Hieu"
            });

        Assert.Equal("Trường Tiểu Học ABC", result.Organization.Name);
        Assert.Equal("Active", result.Organization.VerificationStatus);
        Assert.Equal(2, result.SchoolAdminAccount.Id);
        _membershipRepo.Verify(repo => repo.AddAsync(
            It.Is<OrganizationMembership>(membership =>
                membership.User == schoolAdminAccount
                && membership.Organization != null
                && membership.OrgRole == OrgRole.SchoolAdmin
                && membership.Status == MembershipStatus.Active
                && membership.InvitedByUserId == 1),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _emailSender.Verify(sender => sender.SendAccountProvisionedEmailAsync(
            "admin@schoolx.example", "Nguyen Van Hieu", "Admin Root",
            "Quản trị tổ chức (SchoolAdmin)", "raw-set-password-token",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateOrganizationAsync_NameTooLong_ThrowsBadRequestException()
    {
        _userRepo.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 1, Role = UserRole.Administrator });

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateOrganizationAsync(
            1, new CreateOrganizationRequestDto
            {
                Name = new string('a', 151),
                SchoolAdminUsername = "schooladmin1",
                SchoolAdminEmail = "admin@schoolx.example",
                SchoolAdminFullName = "Nguyen Van Hieu"
            }));
    }

    [Fact]
    public async Task CreateTeacherAccountAsync_CallerIsActiveSchoolAdmin_CreatesTeacherAccountAndMembership()
    {
        _organizationRepo.Setup(repo => repo.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization { Id = 5, Name = "Trường X" });
        _userRepo.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 1, FullName = "Hieu Truong" });
        _membershipRepo.Setup(repo => repo.ExistsAsync(
                It.IsAny<Expression<Func<OrganizationMembership, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var teacherAccount = new UserAccount
        {
            Id = 9,
            Username = "teacher1",
            Email = "teacher1@schoolx.example",
            FullName = "Le Thi B",
            Role = UserRole.Teacher,
            Status = AccountStatus.PasswordResetPending
        };
        _userProvisioningService.Setup(service => service.CreatePendingAccountAsync(
                "teacher1", "teacher1@schoolx.example", "Le Thi B", null,
                UserRole.Teacher, It.IsAny<CancellationToken>()))
            .ReturnsAsync((teacherAccount, "raw-teacher-token"));

        var result = await _sut.CreateTeacherAccountAsync(
            5, 1, new CreateTeacherAccountRequestDto
            {
                Username = "teacher1",
                Email = "teacher1@schoolx.example",
                FullName = "Le Thi B"
            });

        Assert.Equal(9, result.Id);
        Assert.Equal("Teacher", result.Role);
        _membershipRepo.Verify(repo => repo.AddAsync(
            It.Is<OrganizationMembership>(membership =>
                membership.OrganizationId == 5
                && membership.User == teacherAccount
                && membership.OrgRole == OrgRole.Teacher
                && membership.Status == MembershipStatus.Active
                && membership.InvitedByUserId == 1),
            It.IsAny<CancellationToken>()), Times.Once);
        _emailSender.Verify(sender => sender.SendAccountProvisionedEmailAsync(
            "teacher1@schoolx.example", "Le Thi B", "Hieu Truong", "Giáo viên (Teacher)",
            "raw-teacher-token", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateTeacherAccountAsync_CallerNotSchoolAdmin_ThrowsForbiddenException()
    {
        _organizationRepo.Setup(repo => repo.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization { Id = 5, Name = "Trường X" });
        _userRepo.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 1, FullName = "Giao Vien Thuong" });
        _membershipRepo.Setup(repo => repo.ExistsAsync(
                It.IsAny<Expression<Func<OrganizationMembership, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CreateTeacherAccountAsync(
            5, 1, new CreateTeacherAccountRequestDto
            {
                Username = "teacher1",
                Email = "teacher1@schoolx.example",
                FullName = "Le Thi B"
            }));
    }

    [Fact]
    public async Task ListMyOrganizationsAsync_ReturnsActiveMembershipOrganizations()
    {
        _membershipRepo.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<OrganizationMembership, bool>>>(), "Organization",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationMembership>
            {
                new()
                {
                    UserId = 1,
                    Status = MembershipStatus.Active,
                    Organization = new Organization { Id = 5, Name = "Trường X", CreatedByUserId = 1 }
                }
            });

        var result = await _sut.ListMyOrganizationsAsync(1);

        Assert.Single(result);
        Assert.Equal(5, result[0].Id);
    }

    [Fact]
    public async Task GetOrganizationByIdAsync_NonMemberCaller_ThrowsForbiddenException()
    {
        _organizationRepo.Setup(repo => repo.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization { Id = 5, Name = "Trường X", CreatedByUserId = 1 });
        _membershipRepo.Setup(repo => repo.ExistsAsync(
                It.IsAny<Expression<Func<OrganizationMembership, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetOrganizationByIdAsync(5, 999));
    }
}
