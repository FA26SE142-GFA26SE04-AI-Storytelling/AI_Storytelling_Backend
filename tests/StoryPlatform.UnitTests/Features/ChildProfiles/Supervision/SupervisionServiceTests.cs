using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Supervision;

public class SupervisionServiceTests
{
    private readonly Mock<IGenericRepository<SupervisionInvitation>> _invitationRepo = new();
    private readonly Mock<IGenericRepository<SupervisionRelationship>> _relationshipRepo = new();
    private readonly Mock<IGenericRepository<SupervisionPermission>> _permissionRepo = new();
    private readonly Mock<IGenericRepository<UserAccount>> _userRepo = new();
    private readonly Mock<IGenericRepository<ChildProfile>> _profileRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ISupervisionAccessGuard> _guard = new();
    private readonly Mock<IJwtTokenGenerator> _tokenGenerator = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly SupervisionService _sut;

    public SupervisionServiceTests()
    {
        _unitOfWork.Setup(u => u.Repository<SupervisionInvitation>()).Returns(_invitationRepo.Object);
        _unitOfWork.Setup(u => u.Repository<SupervisionRelationship>()).Returns(_relationshipRepo.Object);
        _unitOfWork.Setup(u => u.Repository<SupervisionPermission>()).Returns(_permissionRepo.Object);
        _unitOfWork.Setup(u => u.Repository<UserAccount>()).Returns(_userRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ChildProfile>()).Returns(_profileRepo.Object);
        _tokenGenerator.Setup(t => t.GenerateRefreshToken()).Returns("RANDOM-CODE-0001");
        _sut = new SupervisionService(
            _unitOfWork.Object, _guard.Object, _tokenGenerator.Object, _emailSender.Object);
    }

    [Fact]
    public async Task CreateInvitationAsync_WithoutSupervision_StopsBeforeWrite()
    {
        _guard.Setup(g => g.EnsureActiveSupervisionAsync(1, 2, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không có quyền."));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.CreateInvitationAsync(1, 2, new CreateInvitationRequestDto()));

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateInvitationAsync_Valid_CreatesPendingRandomCode()
    {
        AllowSupervision();
        SupervisionInvitation? added = null;
        _invitationRepo.Setup(r => r.AddAsync(
                It.IsAny<SupervisionInvitation>(), It.IsAny<CancellationToken>()))
            .Callback<SupervisionInvitation, CancellationToken>((value, _) => added = value)
            .ReturnsAsync((SupervisionInvitation value, CancellationToken _) => value);

        var result = await _sut.CreateInvitationAsync(1, 2, new CreateInvitationRequestDto
        {
            InviteeEmail = " someone@example.com ",
            ExpiresInDays = 7
        });

        Assert.Equal("RANDOM-CODE-0001", added!.InvitationCode);
        Assert.Equal("someone@example.com", added.InviteeEmail);
        Assert.Equal(InvitationStatus.Pending, added.Status);
        Assert.True(added.ExpiresAt > DateTime.UtcNow.AddDays(6));
        Assert.Equal("Pending", result.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateInvitationAsync_WithInviteeEmail_SendsInvitationEmail()
    {
        AllowSupervision();
        _userRepo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 2, FullName = "Nguyễn Văn A" });
        _invitationRepo.Setup(r => r.AddAsync(
                It.IsAny<SupervisionInvitation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupervisionInvitation invitation, CancellationToken _) => invitation);

        await _sut.CreateInvitationAsync(1, 2, new CreateInvitationRequestDto
        {
            InviteeEmail = "someone@example.com",
            ExpiresInDays = 7
        });

        _emailSender.Verify(sender => sender.SendSupervisionInvitationEmailAsync(
            "someone@example.com", "Nguyễn Văn A", "RANDOM-CODE-0001",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateInvitationAsync_WithoutInviteeEmail_DoesNotSendEmail()
    {
        AllowSupervision();
        _invitationRepo.Setup(r => r.AddAsync(
                It.IsAny<SupervisionInvitation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupervisionInvitation invitation, CancellationToken _) => invitation);

        await _sut.CreateInvitationAsync(1, 2, new CreateInvitationRequestDto
        {
            InviteeEmail = null,
            ExpiresInDays = 7
        });

        _emailSender.Verify(sender => sender.SendSupervisionInvitationEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AcceptInvitationAsync_UnknownCode_ThrowsBadRequest()
    {
        SetupInvitation(null);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.AcceptInvitationAsync("UNKNOWN", 4));
    }

    [Fact]
    public async Task AcceptInvitationAsync_ExpiredCode_ThrowsBadRequest()
    {
        SetupInvitation(Invitation(DateTime.UtcNow.AddMinutes(-1)));

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.AcceptInvitationAsync("EXPIRED", 4));
    }

    [Fact]
    public async Task AcceptInvitationAsync_ExistingRelationship_RejectsDuplicate()
    {
        SetupValidAcceptance(ChildProfileStatus.Draft, UserRole.Parent);
        _relationshipRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.AcceptInvitationAsync("VALID", 4));

        _relationshipRepo.Verify(r => r.AddAsync(
            It.IsAny<SupervisionRelationship>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(ChildProfileStatus.ReadyForActivation, UserRole.Parent, ChildProfileStatus.ReadyForActivation)]
    [InlineData(ChildProfileStatus.PendingParentConsent, UserRole.Teacher, ChildProfileStatus.PendingParentConsent)]
    [InlineData(ChildProfileStatus.PendingParentConsent, UserRole.Parent, ChildProfileStatus.Active)]
    public async Task AcceptInvitationAsync_Valid_CreatesAdditionalSupervisorAndRechecksBr19(
        ChildProfileStatus initialStatus, UserRole accepterRole, ChildProfileStatus expectedStatus)
    {
        var (invitation, profile) = SetupValidAcceptance(initialStatus, accepterRole);
        SupervisionRelationship? added = null;
        _relationshipRepo.Setup(r => r.AddAsync(
                It.IsAny<SupervisionRelationship>(), It.IsAny<CancellationToken>()))
            .Callback<SupervisionRelationship, CancellationToken>((value, _) => added = value)
            .ReturnsAsync((SupervisionRelationship value, CancellationToken _) => value);

        var result = await _sut.AcceptInvitationAsync("VALID", 4);

        Assert.Equal(InvitationStatus.Accepted, invitation.Status);
        Assert.NotNull(invitation.UsedAt);
        Assert.Equal(4, invitation.InviteeUserId);
        Assert.Equal(SupervisorRole.AdditionalSupervisor, added!.SupervisorRole);
        Assert.Equal(1, added.SupervisionInvitationId);
        Assert.Equal(expectedStatus, profile.Status);
        Assert.Equal("AdditionalSupervisor", result.SupervisorRole);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeSupervisionAsync_CallerNotOwner_ThrowsForbidden()
    {
        var target = AdditionalSupervisor();
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        _guard.Setup(g => g.EnsureOwnerAsync(1, 3, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không phải Owner."));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.RevokeSupervisionAsync(20, 3));
    }

    [Fact]
    public async Task RevokeSupervisionAsync_TargetOwner_ThrowsBadRequest()
    {
        var target = AdditionalSupervisor();
        target.SupervisorRole = SupervisorRole.Owner;
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        AllowOwner();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.RevokeSupervisionAsync(20, 2));

        Assert.Null(target.RevokedAt);
    }

    [Theory]
    [InlineData(UserRole.Teacher, false, ChildProfileStatus.PendingParentConsent)]
    [InlineData(UserRole.Teacher, true, ChildProfileStatus.Active)]
    [InlineData(UserRole.Parent, false, ChildProfileStatus.Active)]
    public async Task RevokeSupervisionAsync_RechecksLastParentSupervisor(
        UserRole ownerRole, bool anotherParentExists, ChildProfileStatus expectedStatus)
    {
        var target = AdditionalSupervisor();
        var profile = new ChildProfile
        {
            Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Active
        };
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _userRepo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 2, Role = ownerRole });
        _relationshipRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(anotherParentExists);
        AllowOwner();

        await _sut.RevokeSupervisionAsync(20, 2);

        Assert.NotNull(target.RevokedAt);
        Assert.Equal(2, target.RevokedByUserId);
        Assert.Equal(expectedStatus, profile.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GrantPermissionAsync_TargetOwner_ThrowsBadRequest()
    {
        var target = AdditionalSupervisor();
        target.SupervisorRole = SupervisorRole.Owner;
        SetupPermissionTarget(target);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.GrantPermissionAsync(20, 2, Permission.ViewProgress));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task GrantPermissionAsync_IsIdempotent(bool alreadyGranted, int expectedAdds)
    {
        SetupPermissionTarget(AdditionalSupervisor());
        _permissionRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<SupervisionPermission, bool>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(alreadyGranted);

        await _sut.GrantPermissionAsync(20, 2, Permission.ViewProgress);

        _permissionRepo.Verify(r => r.AddAsync(
            It.Is<SupervisionPermission>(p => p.SupervisionRelationshipId == 20
                                              && p.Permission == Permission.ViewProgress),
            It.IsAny<CancellationToken>()), Times.Exactly(expectedAdds));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(expectedAdds));
    }

    [Fact]
    public async Task RevokePermissionAsync_DeletesOnlyRequestedPermission()
    {
        SetupPermissionTarget(AdditionalSupervisor());
        var permission = new SupervisionPermission
        {
            Id = 30,
            SupervisionRelationshipId = 20,
            Permission = Permission.ViewProgress
        };
        _permissionRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SupervisionPermission, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync(permission);

        await _sut.RevokePermissionAsync(20, 2, Permission.ViewProgress);

        _permissionRepo.Verify(r => r.Delete(permission), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GrantPermissionAsync_RevokedRelationship_ThrowsBadRequest()
    {
        var target = AdditionalSupervisor();
        target.RevokedAt = DateTime.UtcNow;
        SetupPermissionTarget(target);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.GrantPermissionAsync(20, 2, Permission.ViewProgress));
    }

    [Fact]
    public async Task ListInvitationsAsync_NoActiveSupervision_ThrowsForbidden()
    {
        _guard.Setup(g => g.EnsureActiveSupervisionAsync(1, 9, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không có quyền."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListInvitationsAsync(1, 9));
    }

    [Fact]
    public async Task ListInvitationsAsync_Valid_ReturnsAllInvitationsForChild()
    {
        AllowSupervision();
        _invitationRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<SupervisionInvitation, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupervisionInvitation>
            {
                new() { Id = 1, ChildProfileId = 1, InvitationCode = "CODE-1", Status = InvitationStatus.Pending },
                new() { Id = 2, ChildProfileId = 1, InvitationCode = "CODE-2", Status = InvitationStatus.Accepted }
            });

        var result = await _sut.ListInvitationsAsync(1, 2);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task CancelInvitationAsync_NotPending_ThrowsBadRequest()
    {
        var invitation = new SupervisionInvitation { Id = 1, ChildProfileId = 1, Status = InvitationStatus.Accepted };
        _invitationRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        AllowSupervision();

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CancelInvitationAsync(1, 2));
    }

    [Fact]
    public async Task CancelInvitationAsync_Pending_SetsStatusRevoked()
    {
        var invitation = new SupervisionInvitation { Id = 1, ChildProfileId = 1, Status = InvitationStatus.Pending };
        _invitationRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        AllowSupervision();

        await _sut.CancelInvitationAsync(1, 2);

        Assert.Equal(InvitationStatus.Revoked, invitation.Status);
        Assert.NotNull(invitation.RespondedAt);
    }

    [Fact]
    public async Task ListSupervisorsAsync_Valid_ReturnsOnlyActiveRelationships()
    {
        AllowSupervision();
        _relationshipRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupervisionRelationship>
            {
                new() { Id = 1, ChildProfileId = 1, SupervisorUserId = 2, SupervisorRole = SupervisorRole.Owner, RevokedAt = null }
            });

        var result = await _sut.ListSupervisorsAsync(1, 2);

        Assert.Single(result);
        Assert.Equal("Owner", result[0].SupervisorRole);
    }

    [Fact]
    public async Task ListPermissionsAsync_CallerNotOwner_ThrowsForbidden()
    {
        var target = AdditionalSupervisor();
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        _guard.Setup(g => g.EnsureOwnerAsync(1, 9, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không phải Owner."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListPermissionsAsync(20, 9));
    }

    [Fact]
    public async Task ListPermissionsAsync_Owner_ReturnsGrantedPermissionNames()
    {
        var target = AdditionalSupervisor();
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        AllowOwner();
        _permissionRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<SupervisionPermission, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupervisionPermission> { new() { SupervisionRelationshipId = 20, Permission = Permission.ViewProgress } });

        var result = await _sut.ListPermissionsAsync(20, 2);

        Assert.Single(result);
        Assert.Equal("ViewProgress", result[0]);
    }

    [Fact]
    public async Task ListPermissionsAsync_TargetIsOwnerRelationship_ReturnsAllSystemPermissions()
    {
        var target = new SupervisionRelationship
        {
            Id = 20,
            ChildProfileId = 1,
            SupervisorUserId = 2,
            SupervisorRole = SupervisorRole.Owner
        };
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        AllowOwner();

        var result = await _sut.ListPermissionsAsync(20, 2);

        var expected = Enum.GetValues<Permission>().Select(permission => permission.ToString()).ToList();
        Assert.Equal(expected, result);
        _permissionRepo.Verify(r => r.FindAsync(
            It.IsAny<Expression<Func<SupervisionPermission, bool>>>(), null,
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private void AllowSupervision() => _guard
        .Setup(g => g.EnsureActiveSupervisionAsync(1, 2, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SupervisionRelationship { Id = 10 });

    private void AllowOwner() => _guard
        .Setup(g => g.EnsureOwnerAsync(1, 2, It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    private void SetupInvitation(SupervisionInvitation? invitation) => _invitationRepo
        .Setup(r => r.FirstOrDefaultAsync(
            It.IsAny<Expression<Func<SupervisionInvitation, bool>>>(), null,
            It.IsAny<CancellationToken>())).ReturnsAsync(invitation);

    private (SupervisionInvitation Invitation, ChildProfile Profile) SetupValidAcceptance(
        ChildProfileStatus status, UserRole accepterRole)
    {
        var invitation = Invitation(DateTime.UtcNow.AddDays(3));
        var profile = new ChildProfile { Id = 1, Status = status };
        SetupInvitation(invitation);
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _userRepo.Setup(r => r.GetByIdAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 4, Role = accepterRole });
        _relationshipRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(false);
        return (invitation, profile);
    }

    private void SetupPermissionTarget(SupervisionRelationship target)
    {
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        AllowOwner();
    }

    private static SupervisionInvitation Invitation(DateTime expiresAt) => new()
    {
        Id = 1,
        ChildProfileId = 1,
        InvitationCode = "VALID",
        Status = InvitationStatus.Pending,
        ExpiresAt = expiresAt
    };

    private static SupervisionRelationship AdditionalSupervisor() => new()
    {
        Id = 20,
        ChildProfileId = 1,
        SupervisorUserId = 5,
        SupervisorRole = SupervisorRole.AdditionalSupervisor
    };
}
