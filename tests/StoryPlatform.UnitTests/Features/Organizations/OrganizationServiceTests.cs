using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
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
    private readonly OrganizationService _sut;

    public OrganizationServiceTests()
    {
        _unitOfWork.Setup(work => work.Repository<Organization>()).Returns(_organizationRepo.Object);
        _unitOfWork.Setup(work => work.Repository<OrganizationMembership>()).Returns(_membershipRepo.Object);
        _unitOfWork.Setup(work => work.Repository<UserAccount>()).Returns(_userRepo.Object);
        _sut = new OrganizationService(_unitOfWork.Object);
    }

    [Fact]
    public async Task CreateOrganizationAsync_ValidRequest_CreatesOrganizationAndOwnerMembership()
    {
        _userRepo.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 1, Role = UserRole.Teacher });

        var result = await _sut.CreateOrganizationAsync(
            1, new CreateOrganizationRequestDto { Name = "  Trường Tiểu Học ABC  " });

        Assert.Equal("Trường Tiểu Học ABC", result.Name);
        Assert.Equal("PendingVerification", result.VerificationStatus);
        _membershipRepo.Verify(repo => repo.AddAsync(
            It.Is<OrganizationMembership>(membership =>
                membership.UserId == 1
                && membership.Organization != null
                && membership.OrgRole == OrgRole.SchoolAdmin
                && membership.Status == MembershipStatus.Active),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateOrganizationAsync_NameTooLong_ThrowsBadRequestException()
    {
        _userRepo.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 1, Role = UserRole.Teacher });

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateOrganizationAsync(
            1, new CreateOrganizationRequestDto { Name = new string('a', 151) }));
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
