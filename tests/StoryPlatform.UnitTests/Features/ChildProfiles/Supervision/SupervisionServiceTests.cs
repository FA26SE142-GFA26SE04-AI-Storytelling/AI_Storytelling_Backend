using System.Linq.Expressions;
using System.Text.Json;
using Moq;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Security;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Services;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Supervision;

public class SupervisionServiceTests
{
    private readonly Mock<IGenericRepository<SupervisionInvitation>> _invitationRepo = new();
    private readonly Mock<IGenericRepository<SupervisionRelationship>> _relationshipRepo = new();
    private readonly Mock<IGenericRepository<SupervisionPermission>> _permissionRepo = new();
    private readonly Mock<IGenericRepository<SupervisionPermissionRequest>> _permissionRequestRepo = new();
    private readonly Mock<IGenericRepository<OwnershipTransferRequest>> _ownershipTransferRequestRepo = new();
    private readonly Mock<IGenericRepository<UserAccount>> _userRepo = new();
    private readonly Mock<IGenericRepository<ChildProfile>> _profileRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ISupervisionAccessGuard> _guard = new();
    private readonly Mock<IJwtTokenGenerator> _tokenGenerator = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<IAuditLogWriter> _auditLogWriter = new();
    private readonly List<(int? Actor, string Action, string EntityType, int EntityId, string? Before, string? After)> _audits = new();
    private readonly SupervisionService _sut;

    public SupervisionServiceTests()
    {
        _unitOfWork.Setup(u => u.Repository<SupervisionInvitation>()).Returns(_invitationRepo.Object);
        _unitOfWork.Setup(u => u.Repository<SupervisionRelationship>()).Returns(_relationshipRepo.Object);
        _unitOfWork.Setup(u => u.Repository<SupervisionPermission>()).Returns(_permissionRepo.Object);
        _unitOfWork.Setup(u => u.Repository<SupervisionPermissionRequest>()).Returns(_permissionRequestRepo.Object);
        _unitOfWork.Setup(u => u.Repository<OwnershipTransferRequest>()).Returns(_ownershipTransferRequestRepo.Object);
        _unitOfWork.Setup(u => u.Repository<UserAccount>()).Returns(_userRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ChildProfile>()).Returns(_profileRepo.Object);
        _tokenGenerator.Setup(t => t.GenerateRefreshToken()).Returns("RANDOM-CODE-0001");
        _auditLogWriter.Setup(w => w.LogAsync(
                It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback<int?, string, string, int, object?, object?, CancellationToken>(
                (actor, action, entityType, entityId, before, after, _) => _audits.Add((
                    actor, action, entityType, entityId,
                    before == null ? null : JsonSerializer.Serialize(before),
                    after == null ? null : JsonSerializer.Serialize(after))))
            .Returns(Task.CompletedTask);
        _sut = new SupervisionService(
            _unitOfWork.Object, _guard.Object, _tokenGenerator.Object, _emailSender.Object,
            _notificationService.Object, _auditLogWriter.Object);
    }

    [Fact]
    public async Task CreateInvitationAsync_WithoutOwnerAccess_StopsBeforeWrite()
    {
        _guard.Setup(g => g.EnsureOwnerAsync(1, 2, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không phải Owner."));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.CreateInvitationAsync(1, 2, new CreateInvitationRequestDto
            {
                InviteeEmail = "someone@example.com"
            }));

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_audits);
    }

    [Fact]
    public async Task CreateInvitationAsync_Valid_CreatesPendingRandomCode()
    {
        AllowOwner();
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
        var audit = Assert.Single(_audits);
        Assert.Equal("CREATE_SUPERVISION_INVITATION", audit.Action);
        Assert.Contains("s*****@example.com", audit.After);
        Assert.DoesNotContain("someone@example.com", audit.After);
    }

    [Fact]
    public async Task CreateInvitationAsync_WithInviteeEmail_SendsInvitationEmail()
    {
        AllowOwner();
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
    public async Task CreateInvitationAsync_WithoutInviteeEmail_ThrowsBadRequest()
    {
        AllowOwner();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.CreateInvitationAsync(1, 2, new CreateInvitationRequestDto
            {
                InviteeEmail = null,
                ExpiresInDays = 7
            }));

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_audits);
    }

    [Fact]
    public async Task CreateInvitationAsync_InvalidInviteeEmail_DoesNotPersistOrSendEmail()
    {
        AllowOwner();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.CreateInvitationAsync(1, 2, new CreateInvitationRequestDto
            {
                InviteeEmail = "not-an-email",
                ExpiresInDays = 7
            }));

        _invitationRepo.Verify(r => r.AddAsync(
            It.IsAny<SupervisionInvitation>(), It.IsAny<CancellationToken>()), Times.Never);
        _emailSender.Verify(sender => sender.SendSupervisionInvitationEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClaimInvitationAsync_Valid_StoresHashedOtpAndSendsEmail()
    {
        var invitation = Invitation(DateTime.UtcNow.AddDays(3));
        SetupInvitation(invitation);
        string? sentOtp = null;
        _emailSender.Setup(sender => sender.SendSupervisionInvitationOtpEmailAsync(
                "invitee@example.com", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, otp, _) => sentOtp = otp)
            .Returns(Task.CompletedTask);

        var result = await _sut.ClaimInvitationAsync("VALID", 4);

        Assert.NotNull(sentOtp);
        Assert.Matches("^[0-9]{6}$", sentOtp!);
        Assert.Equal(TokenHasher.Hash($"1:{sentOtp}"), invitation.OtpHash);
        Assert.NotNull(invitation.OtpExpiresAt);
        Assert.Equal("i*****@example.com", result.MaskedEmail);
        var audit = Assert.Single(_audits);
        Assert.Equal("CLAIM_SUPERVISION_INVITATION", audit.Action);
        Assert.DoesNotContain(sentOtp!, audit.After);
        Assert.DoesNotContain("invitee@example.com", audit.After);
    }

    [Fact]
    public async Task VerifyInvitationOtpAsync_CorrectOtp_MarksVerifiedAndReturnsPreview()
    {
        var invitation = Invitation(DateTime.UtcNow.AddDays(3));
        invitation.OtpHash = TokenHasher.Hash("1:123456");
        invitation.OtpExpiresAt = DateTime.UtcNow.AddMinutes(5);
        SetupInvitation(invitation);
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, Nickname = "Bé An", AgeBand = AgeBand.Age_6_8 });
        _userRepo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 2, FullName = "Chị Mai" });

        var result = await _sut.VerifyInvitationOtpAsync("VALID", "123456", 4);

        Assert.Equal(4, invitation.InviteeUserId);
        Assert.NotNull(invitation.OtpVerifiedAt);
        Assert.Null(invitation.OtpHash);
        Assert.Equal("Bé An", result.ChildNickname);
        Assert.Single(_audits, audit => audit.Action == "VERIFY_SUPERVISION_INVITATION_OTP");
    }

    [Fact]
    public async Task VerifyInvitationOtpAsync_WrongOtp_LocksAfterMaxAttempts()
    {
        var invitation = Invitation(DateTime.UtcNow.AddDays(3));
        invitation.OtpHash = TokenHasher.Hash("1:123456");
        invitation.OtpExpiresAt = DateTime.UtcNow.AddMinutes(5);
        invitation.OtpFailedAttempts = SupervisionDefaults.InvitationOtpMaxAttempts - 1;
        SetupInvitation(invitation);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.VerifyInvitationOtpAsync("VALID", "999999", 4));

        Assert.Null(invitation.OtpHash);
        Assert.Null(invitation.OtpExpiresAt);
        Assert.Single(_audits, audit => audit.Action == "SUPERVISION_INVITATION_OTP_LOCKED");
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
    public async Task AcceptInvitationAsync_WithoutVerifiedOtp_ThrowsBadRequest()
    {
        SetupInvitation(Invitation(DateTime.UtcNow.AddDays(3)));

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.AcceptInvitationAsync("VALID", 4));

        Assert.Empty(_audits);
    }

    [Fact]
    public async Task AcceptInvitationAsync_VerifiedByDifferentUser_ThrowsBadRequest()
    {
        SetupInvitation(VerifiedInvitation(verifiedUserId: 9));

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.AcceptInvitationAsync("VALID", 4));
    }

    [Fact]
    public async Task AcceptInvitationAsync_VerificationWindowPassed_ThrowsBadRequest()
    {
        SetupInvitation(VerifiedInvitation(
            verifiedAt: DateTime.UtcNow - SupervisionDefaults.InvitationVerifiedWindow - TimeSpan.FromMinutes(1)));

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.AcceptInvitationAsync("VALID", 4));
    }

    [Fact]
    public async Task AcceptInvitationAsync_Valid_CreatesAdditionalSupervisorWithDefaultPresetAndNotifiesOwner()
    {
        var invitation = SetupVerifiedAcceptance();
        var addedPermissions = new List<SupervisionPermission>();
        _relationshipRepo.Setup(r => r.AddAsync(
                It.IsAny<SupervisionRelationship>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupervisionRelationship value, CancellationToken _) => value);
        _permissionRepo.Setup(r => r.AddAsync(
                It.IsAny<SupervisionPermission>(), It.IsAny<CancellationToken>()))
            .Callback<SupervisionPermission, CancellationToken>((value, _) => addedPermissions.Add(value))
            .ReturnsAsync((SupervisionPermission value, CancellationToken _) => value);
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, OwnerUserId = 2, Nickname = "Bé An" });
        _userRepo.Setup(r => r.GetByIdAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 4, FullName = "Cô Mai" });

        var result = await _sut.AcceptInvitationAsync("VALID", 4);

        Assert.Equal(InvitationStatus.Accepted, invitation.Status);
        Assert.Equal("AdditionalSupervisor", result.SupervisorRole);
        Assert.Equal(
            SupervisionDefaults.DefaultPreset.OrderBy(value => value),
            addedPermissions.Select(value => value.Permission).OrderBy(value => value));
        _notificationService.Verify(n => n.CreateAsync(
            2, NotificationType.SupervisionAccepted,
            It.Is<string>(payload => payload.Contains("\"supervisorUserId\":4")),
            It.IsAny<CancellationToken>()), Times.Once);
        var audit = Assert.Single(_audits);
        Assert.Equal("ACCEPT_SUPERVISION_INVITATION", audit.Action);
        Assert.Contains("AdditionalSupervisor", audit.After);
        Assert.Contains("ApproveStory", audit.After);
    }

    [Fact]
    public async Task RejectInvitationAsync_WithoutVerifiedOtp_ThrowsBadRequest()
    {
        SetupInvitation(Invitation(DateTime.UtcNow.AddDays(3)));

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.RejectInvitationAsync("VALID", 4));

        Assert.Empty(_audits);
    }

    [Fact]
    public async Task RejectInvitationAsync_Verified_SetsRejectedNotifiesOwnerAndCreatesNoRelationship()
    {
        var invitation = VerifiedInvitation();
        SetupInvitation(invitation);
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, OwnerUserId = 2 });
        _userRepo.Setup(r => r.GetByIdAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 4, FullName = "Cô Mai" });

        await _sut.RejectInvitationAsync("VALID", 4);

        Assert.Equal(InvitationStatus.Rejected, invitation.Status);
        _relationshipRepo.Verify(r => r.AddAsync(
            It.IsAny<SupervisionRelationship>(), It.IsAny<CancellationToken>()), Times.Never);
        _notificationService.Verify(n => n.CreateAsync(
            2, NotificationType.SupervisionRejected, It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(_audits, audit => audit.Action == "REJECT_SUPERVISION_INVITATION");
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

    [Fact]
    public async Task CreatePermissionRequestAsync_CallerNotRelationshipOwner_ThrowsForbidden()
    {
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdditionalSupervisor());

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CreatePermissionRequestAsync(
            20, 999, new CreatePermissionRequestRequestDto
            {
                Permissions = new List<Permission> { Permission.ViewProgress }
            }));
    }

    [Fact]
    public async Task CreatePermissionRequestAsync_TargetIsOwnerRelationship_ThrowsBadRequest()
    {
        var relationship = AdditionalSupervisor();
        relationship.SupervisorRole = SupervisorRole.Owner;
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(relationship);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreatePermissionRequestAsync(
            20, 5, new CreatePermissionRequestRequestDto
            {
                Permissions = new List<Permission> { Permission.ViewProgress }
            }));
    }

    [Fact]
    public async Task CreatePermissionRequestAsync_EmptyPermissionList_ThrowsBadRequest()
    {
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdditionalSupervisor());

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreatePermissionRequestAsync(
            20, 5, new CreatePermissionRequestRequestDto()));
    }

    [Fact]
    public async Task CreatePermissionRequestAsync_Valid_CreatesPendingDistinctPermissionsAndNotifiesOwner()
    {
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdditionalSupervisor());
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, OwnerUserId = 2 });
        SupervisionPermissionRequest? added = null;
        _permissionRequestRepo.Setup(r => r.AddAsync(
                It.IsAny<SupervisionPermissionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SupervisionPermissionRequest, CancellationToken>((value, _) => added = value)
            .ReturnsAsync((SupervisionPermissionRequest value, CancellationToken _) => value);

        var result = await _sut.CreatePermissionRequestAsync(
            20, 5, new CreatePermissionRequestRequestDto
            {
                Permissions = new List<Permission>
                {
                    Permission.ViewProgress,
                    Permission.ViewProgress,
                    Permission.ViewResults
                }
            });

        Assert.Equal(PermissionRequestStatus.Pending, added!.Status);
        Assert.Equal(5, added.RequesterUserId);
        Assert.Equal(2, added.Items.Count);
        Assert.Equal(2, result.Permissions.Count);
        _notificationService.Verify(n => n.CreateAsync(
            2, NotificationType.PermissionRequestCreated, It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcceptPermissionRequestAsync_NotOwner_ThrowsForbidden()
    {
        var request = PendingPermissionRequest();
        SetupPermissionRequest(request);
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdditionalSupervisor());
        _guard.Setup(g => g.EnsureOwnerAsync(1, 9, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không phải Owner."));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.AcceptPermissionRequestAsync(30, 9));
    }

    [Fact]
    public async Task AcceptPermissionRequestAsync_AlreadyResponded_ThrowsBadRequest()
    {
        var request = PendingPermissionRequest();
        request.Status = PermissionRequestStatus.Accepted;
        SetupPermissionRequest(request);
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdditionalSupervisor());
        AllowOwner();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.AcceptPermissionRequestAsync(30, 2));
    }

    [Fact]
    public async Task AcceptPermissionRequestAsync_Valid_GrantsAllIdempotentlyAndNotifiesRequester()
    {
        var request = PendingPermissionRequest();
        SetupPermissionRequest(request);
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdditionalSupervisor());
        AllowOwner();
        _permissionRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<SupervisionPermission, bool>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _sut.AcceptPermissionRequestAsync(30, 2);

        Assert.Equal("Accepted", result.Status);
        Assert.Equal(PermissionRequestStatus.Accepted, request.Status);
        Assert.Equal(2, request.RespondedByUserId);
        _permissionRepo.Verify(r => r.AddAsync(
            It.IsAny<SupervisionPermission>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _notificationService.Verify(n => n.CreateAsync(
            5, NotificationType.PermissionRequestAccepted, It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectPermissionRequestAsync_Valid_SetsRejectedAndNotifiesRequester()
    {
        var request = PendingPermissionRequest();
        SetupPermissionRequest(request);
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdditionalSupervisor());
        AllowOwner();

        var result = await _sut.RejectPermissionRequestAsync(30, 2);

        Assert.Equal("Rejected", result.Status);
        Assert.Equal(PermissionRequestStatus.Rejected, request.Status);
        _notificationService.Verify(n => n.CreateAsync(
            5, NotificationType.PermissionRequestRejected, It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelPermissionRequestAsync_ByRequesterWhilePending_SetsCancelled()
    {
        var request = PendingPermissionRequest();
        SetupPermissionRequest(request);

        var before = DateTime.UtcNow;
        var result = await _sut.CancelPermissionRequestAsync(30, 5);

        Assert.Equal("Cancelled", result.Status);
        Assert.Equal(PermissionRequestStatus.Cancelled, request.Status);
        Assert.Equal(5, request.RespondedByUserId);
        Assert.True(request.RespondedAt >= before);
        _permissionRequestRepo.Verify(r => r.Update(request), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _permissionRepo.Verify(r => r.AddAsync(
            It.IsAny<SupervisionPermission>(), It.IsAny<CancellationToken>()), Times.Never);
        _notificationService.Verify(n => n.CreateAsync(
            It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(2)] // Owner
    [InlineData(9)] // người không liên quan
    public async Task CancelPermissionRequestAsync_NotRequester_ThrowsForbidden(int currentUserId)
    {
        var request = PendingPermissionRequest();
        SetupPermissionRequest(request);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.CancelPermissionRequestAsync(30, currentUserId));

        Assert.Equal(PermissionRequestStatus.Pending, request.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(PermissionRequestStatus.Accepted)]
    [InlineData(PermissionRequestStatus.Rejected)]
    [InlineData(PermissionRequestStatus.Cancelled)]
    public async Task CancelPermissionRequestAsync_NotPending_ThrowsBadRequest(PermissionRequestStatus status)
    {
        var request = PendingPermissionRequest();
        request.Status = status;
        SetupPermissionRequest(request);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.CancelPermissionRequestAsync(30, 5));

        Assert.Equal(status, request.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelPermissionRequestAsync_NotFound_ThrowsNotFound()
    {
        _permissionRequestRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SupervisionPermissionRequest, bool>>>(), "Items",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupervisionPermissionRequest?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.CancelPermissionRequestAsync(30, 5));
    }

    [Fact]
    public async Task AcceptPermissionRequestAsync_Cancelled_ThrowsBadRequest()
    {
        var request = PendingPermissionRequest();
        request.Status = PermissionRequestStatus.Cancelled;
        SetupPermissionRequest(request);
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdditionalSupervisor());
        AllowOwner();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.AcceptPermissionRequestAsync(30, 2));

        _permissionRepo.Verify(r => r.AddAsync(
            It.IsAny<SupervisionPermission>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListPermissionRequestsAsync_Valid_ReturnsRequestsForRelationship()
    {
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdditionalSupervisor());
        AllowSupervision();
        _permissionRequestRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<SupervisionPermissionRequest, bool>>>(), "Items",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupervisionPermissionRequest> { PendingPermissionRequest() });

        var result = await _sut.ListPermissionRequestsAsync(20, 2);

        Assert.Single(result);
    }

    [Fact]
    public async Task RequestOwnershipTransferAsync_TargetIsSameAsActor_ThrowsBadRequestException()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.RequestOwnershipTransferAsync(
            1, 2, new TransferOwnershipRequestDto { TargetSupervisorUserId = 2 }));

        _guard.Verify(g => g.EnsureOwnerAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestOwnershipTransferAsync_AdditionalSupervisor_ThrowsForbidden()
    {
        _guard.Setup(g => g.EnsureOwnerAsync(1, 3, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không phải Owner."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.RequestOwnershipTransferAsync(
            1, 3, new TransferOwnershipRequestDto { TargetSupervisorUserId = 2 }));

        _ownershipTransferRequestRepo.Verify(r => r.AddAsync(
            It.IsAny<OwnershipTransferRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestOwnershipTransferAsync_TargetNotActiveAdditionalSupervisor_ThrowsBadRequestException()
    {
        AllowOwner();
        _relationshipRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.RequestOwnershipTransferAsync(
            1, 2, new TransferOwnershipRequestDto { TargetSupervisorUserId = 3 }));
    }

    [Fact]
    public async Task RequestOwnershipTransferAsync_ExistingPendingRequest_ThrowsBadRequest()
    {
        AllowOwner();
        _relationshipRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _ownershipTransferRequestRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<OwnershipTransferRequest, bool>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.RequestOwnershipTransferAsync(
            1, 2, new TransferOwnershipRequestDto { TargetSupervisorUserId = 3 }));
    }

    [Fact]
    public async Task RequestOwnershipTransferAsync_Valid_CreatesPendingRequestWithExpiryAndNotifiesTarget()
    {
        AllowOwner();
        _relationshipRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(true);
        OwnershipTransferRequest? added = null;
        _ownershipTransferRequestRepo.Setup(r => r.AddAsync(
                It.IsAny<OwnershipTransferRequest>(), It.IsAny<CancellationToken>()))
            .Callback<OwnershipTransferRequest, CancellationToken>((value, _) => added = value)
            .ReturnsAsync((OwnershipTransferRequest value, CancellationToken _) => value);

        var result = await _sut.RequestOwnershipTransferAsync(
            1, 2, new TransferOwnershipRequestDto { TargetSupervisorUserId = 3 });

        Assert.Equal(OwnershipTransferRequestStatus.Pending, added!.Status);
        Assert.True(added.ExpiresAt > DateTime.UtcNow.AddDays(6));
        Assert.Equal(added.ExpiresAt, result.ExpiresAt);
        Assert.Equal(2, result.RequesterUserId);
        Assert.Equal(3, result.ResponderUserId);
        _notificationService.Verify(n => n.CreateAsync(
            3, NotificationType.OwnershipTransferRequested, It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(_audits, audit => audit.Action == "REQUEST_OWNERSHIP_TRANSFER");
    }

    [Fact]
    public async Task AcceptOwnershipTransferAsync_NotTargetUser_ThrowsForbidden()
    {
        _ownershipTransferRequestRepo.Setup(r => r.GetByIdAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PendingTransferRequest());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.AcceptOwnershipTransferAsync(50, 999));
    }

    [Fact]
    public async Task AcceptOwnershipTransferAsync_RequestNotPending_ThrowsBadRequest()
    {
        var transferRequest = PendingTransferRequest();
        transferRequest.Status = OwnershipTransferRequestStatus.Accepted;
        _ownershipTransferRequestRepo.Setup(r => r.GetByIdAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transferRequest);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.AcceptOwnershipTransferAsync(50, 3));
    }

    [Fact]
    public async Task AcceptOwnershipTransferAsync_Expired_MarksExpiredAndThrows()
    {
        var transferRequest = PendingTransferRequest();
        transferRequest.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        _ownershipTransferRequestRepo.Setup(r => r.GetByIdAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transferRequest);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.AcceptOwnershipTransferAsync(50, 3));

        Assert.Equal(OwnershipTransferRequestStatus.Expired, transferRequest.Status);
        var audit = Assert.Single(_audits);
        Assert.Null(audit.Actor);
        Assert.Equal("EXPIRE_OWNERSHIP_TRANSFER", audit.Action);
    }

    [Fact]
    public async Task AcceptOwnershipTransferAsync_Valid_SwapsRolesGivesPresetToPreviousOwnerAndNotifies()
    {
        var transferRequest = PendingTransferRequest();
        _ownershipTransferRequestRepo.Setup(r => r.GetByIdAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transferRequest);
        var ownerRelationship = new SupervisionRelationship
        {
            Id = 10, ChildProfileId = 1, SupervisorUserId = 2, SupervisorRole = SupervisorRole.Owner
        };
        var targetRelationship = new SupervisionRelationship
        {
            Id = 20, ChildProfileId = 1, SupervisorUserId = 3,
            SupervisorRole = SupervisorRole.AdditionalSupervisor
        };
        _relationshipRepo.SetupSequence(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ownerRelationship)
            .ReturnsAsync(targetRelationship);
        _permissionRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<SupervisionPermission, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupervisionPermission>());
        var addedPermissions = new List<SupervisionPermission>();
        _permissionRepo.Setup(r => r.AddAsync(
                It.IsAny<SupervisionPermission>(), It.IsAny<CancellationToken>()))
            .Callback<SupervisionPermission, CancellationToken>((value, _) => addedPermissions.Add(value))
            .ReturnsAsync((SupervisionPermission value, CancellationToken _) => value);
        var childProfile = new ChildProfile { Id = 1, OwnerUserId = 2, Status = ChildProfileStatus.Active };
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(childProfile);

        var result = await _sut.AcceptOwnershipTransferAsync(50, 3);

        Assert.Equal("Owner", result.SupervisorRole);
        Assert.Equal(SupervisorRole.AdditionalSupervisor, ownerRelationship.SupervisorRole);
        Assert.Equal(SupervisorRole.Owner, targetRelationship.SupervisorRole);
        Assert.Equal(3, childProfile.OwnerUserId);
        Assert.Equal(
            SupervisionDefaults.DefaultPreset.OrderBy(value => value),
            addedPermissions.Select(value => value.Permission).OrderBy(value => value));
        _unitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _notificationService.Verify(n => n.CreateAsync(
            2, NotificationType.OwnershipTransferAccepted, It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        var audit = Assert.Single(_audits);
        Assert.Equal("ACCEPT_OWNERSHIP_TRANSFER", audit.Action);
        Assert.Contains("\"ownerUserId\":2", audit.Before);
        Assert.Contains("\"ownerUserId\":3", audit.After);
    }

    [Fact]
    public async Task AcceptOwnershipTransferAsync_RequesterNoLongerOwner_ThrowsBadRequestAndRollsBack()
    {
        var transferRequest = PendingTransferRequest();
        _ownershipTransferRequestRepo.Setup(r => r.GetByIdAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transferRequest);
        _relationshipRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupervisionRelationship?)null);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.AcceptOwnershipTransferAsync(50, 3));

        _unitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcceptOwnershipTransferAsync_TargetNoLongerActiveAdditionalSupervisor_ThrowsBadRequestAndRollsBack()
    {
        var transferRequest = PendingTransferRequest();
        _ownershipTransferRequestRepo.Setup(r => r.GetByIdAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transferRequest);
        var ownerRelationship = new SupervisionRelationship
        {
            Id = 10, ChildProfileId = 1, SupervisorUserId = 2, SupervisorRole = SupervisorRole.Owner
        };
        _relationshipRepo.SetupSequence(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ownerRelationship)
            .ReturnsAsync((SupervisionRelationship?)null);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.AcceptOwnershipTransferAsync(50, 3));

        _unitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectOwnershipTransferAsync_Valid_SetsRejectedAndNotifiesRequester()
    {
        var transferRequest = PendingTransferRequest();
        _ownershipTransferRequestRepo.Setup(r => r.GetByIdAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transferRequest);

        var result = await _sut.RejectOwnershipTransferAsync(50, 3);

        Assert.Equal("Rejected", result.Status);
        _notificationService.Verify(n => n.CreateAsync(
            2, NotificationType.OwnershipTransferRejected, It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(_audits, audit => audit.Action == "REJECT_OWNERSHIP_TRANSFER");
    }

    [Fact]
    public async Task CancelOwnershipTransferAsync_ByCurrentOwnerWhilePending_SetsCancelled()
    {
        var transferRequest = PendingTransferRequest();
        _ownershipTransferRequestRepo.Setup(r => r.GetByIdAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transferRequest);
        AllowOwner();

        var result = await _sut.CancelOwnershipTransferAsync(50, 2);

        Assert.Equal(OwnershipTransferRequestStatus.Cancelled, transferRequest.Status);
        Assert.Equal("Cancelled", result.Status);
        Assert.Single(_audits, audit => audit.Action == "CANCEL_OWNERSHIP_TRANSFER");
    }

    [Fact]
    public async Task ListOwnershipTransferRequestsAsync_ExpiredPending_MapsExpired()
    {
        AllowSupervision();
        var request = PendingTransferRequest();
        request.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        _ownershipTransferRequestRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<OwnershipTransferRequest, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OwnershipTransferRequest> { request });

        var result = await _sut.ListOwnershipTransferRequestsAsync(1, 2);

        Assert.Equal("Expired", Assert.Single(result).Status);
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

    private static SupervisionInvitation VerifiedInvitation(
        int verifiedUserId = 4, DateTime? verifiedAt = null)
    {
        var invitation = Invitation(DateTime.UtcNow.AddDays(3));
        invitation.OtpVerifiedAt = verifiedAt ?? DateTime.UtcNow.AddMinutes(-1);
        invitation.InviteeUserId = verifiedUserId;
        return invitation;
    }

    private SupervisionInvitation SetupVerifiedAcceptance()
    {
        var invitation = VerifiedInvitation();
        SetupInvitation(invitation);
        _profileRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, Status = ChildProfileStatus.Active });
        _relationshipRepo.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(false);
        return invitation;
    }

    private void SetupPermissionTarget(SupervisionRelationship target)
    {
        _relationshipRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        AllowOwner();
    }

    private void SetupPermissionRequest(SupervisionPermissionRequest request) =>
        _permissionRequestRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SupervisionPermissionRequest, bool>>>(), "Items",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

    private void SetupOwnerRelationship() => _guard
        .Setup(g => g.EnsureActiveSupervisionAsync(1, 2, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SupervisionRelationship
        {
            Id = 10,
            ChildProfileId = 1,
            SupervisorUserId = 2,
            SupervisorRole = SupervisorRole.Owner
        });

    private static SupervisionInvitation Invitation(DateTime expiresAt) => new()
    {
        Id = 1,
        ChildProfileId = 1,
        InvitationCode = "VALID",
        InviteeEmail = "invitee@example.com",
        InviterUserId = 2,
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

    private static SupervisionPermissionRequest PendingPermissionRequest() => new()
    {
        Id = 30,
        SupervisionRelationshipId = 20,
        RequesterUserId = 5,
        Status = PermissionRequestStatus.Pending,
        Items = new List<SupervisionPermissionRequestItem>
        {
            new() { Permission = Permission.ViewProgress },
            new() { Permission = Permission.ViewResults }
        }
    };

    private static OwnershipTransferRequest PendingTransferRequest() => new()
    {
        Id = 50,
        ChildProfileId = 1,
        CurrentOwnerUserId = 2,
        TargetSupervisorUserId = 3,
        Status = OwnershipTransferRequestStatus.Pending,
        ExpiresAt = DateTime.UtcNow.AddDays(3)
    };

    private static OwnershipTransferRequest PendingOwnerResponseTransferRequest() => new()
    {
        Id = 50,
        ChildProfileId = 1,
        CurrentOwnerUserId = 2,
        TargetSupervisorUserId = 3,
        Status = OwnershipTransferRequestStatus.PendingOwnerResponse
    };
}
