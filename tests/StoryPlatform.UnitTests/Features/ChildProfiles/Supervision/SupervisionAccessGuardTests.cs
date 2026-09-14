using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Supervision;

public class SupervisionAccessGuardTests
{
    private readonly Mock<IGenericRepository<SupervisionRelationship>> _relationshipRepo = new();
    private readonly Mock<IGenericRepository<SupervisionPermission>> _permissionRepo = new();
    private readonly SupervisionAccessGuard _sut;

    public SupervisionAccessGuardTests()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<SupervisionRelationship>()).Returns(_relationshipRepo.Object);
        unitOfWork.Setup(u => u.Repository<SupervisionPermission>()).Returns(_permissionRepo.Object);
        _sut = new SupervisionAccessGuard(unitOfWork.Object);
    }

    [Fact]
    public async Task EnsureActiveSupervisionAsync_MissingRelationship_ThrowsForbidden()
    {
        SetupRelationship(null);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.EnsureActiveSupervisionAsync(1, 2));
    }

    [Fact]
    public async Task EnsureOwnerAsync_AdditionalSupervisor_ThrowsForbidden()
    {
        SetupRelationship(Relationship(SupervisorRole.AdditionalSupervisor));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.EnsureOwnerAsync(1, 2));
    }

    [Fact]
    public async Task EnsurePermissionAsync_Owner_DoesNotQueryPermissionTable()
    {
        SetupRelationship(Relationship(SupervisorRole.Owner));

        await _sut.EnsurePermissionAsync(1, 2, Permission.ManageSafetySettings);

        _permissionRepo.Verify(r => r.ExistsAsync(
            It.IsAny<Expression<Func<SupervisionPermission, bool>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task EnsurePermissionAsync_AdditionalSupervisor_RequiresExactPermission(
        bool granted, bool shouldThrow)
    {
        SetupRelationship(Relationship(SupervisorRole.AdditionalSupervisor));
        _permissionRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<SupervisionPermission, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(granted);

        var action = () => _sut.EnsurePermissionAsync(1, 2, Permission.ViewProgress);

        if (shouldThrow)
        {
            await Assert.ThrowsAsync<ForbiddenException>(action);
        }
        else
        {
            await action();
        }
    }

    private void SetupRelationship(SupervisionRelationship? relationship) => _relationshipRepo
        .Setup(r => r.FirstOrDefaultAsync(
            It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(), null,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(relationship);

    private static SupervisionRelationship Relationship(SupervisorRole role) => new()
    {
        Id = 10,
        ChildProfileId = 1,
        SupervisorUserId = 2,
        SupervisorRole = role
    };
}
