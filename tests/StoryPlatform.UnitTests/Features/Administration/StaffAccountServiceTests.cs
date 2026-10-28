using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Security;
using StoryPlatform.Application.Features.Administration.DTOs;
using StoryPlatform.Application.Features.Administration.Services;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Auth.Interfaces;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Administration;

public class StaffAccountServiceTests
{
    private readonly Mock<IGenericRepository<UserAccount>> _userRepoMock = new();
    private readonly Mock<IGenericRepository<RefreshToken>> _refreshTokenRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IUserProvisioningService> _provisioningMock = new();
    private readonly Mock<IJwtTokenGenerator> _jwtGeneratorMock = new();
    private readonly Mock<IEmailSender> _emailSenderMock = new();
    private readonly Mock<IAuditLogWriter> _auditLogWriterMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<IModerationSweepService> _sweepServiceMock = new();
    private readonly StaffAccountService _sut;

    private readonly UserAccount _admin = new()
    {
        Id = 1,
        Username = "admin_demo",
        Email = "admin@example.com",
        FullName = "Admin Demo",
        Role = UserRole.Administrator,
        Status = AccountStatus.Active
    };

    public StaffAccountServiceTests()
    {
        _unitOfWorkMock.Setup(u => u.Repository<UserAccount>())
            .Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Repository<RefreshToken>())
            .Returns(_refreshTokenRepoMock.Object);
        _userRepoMock.Setup(r => r.GetByIdAsync(
                _admin.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_admin);
        _refreshTokenRepoMock
            .Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<RefreshToken, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RefreshToken>());
        _sut = new StaffAccountService(
            _unitOfWorkMock.Object,
            _provisioningMock.Object,
            _jwtGeneratorMock.Object,
            _emailSenderMock.Object,
            _auditLogWriterMock.Object,
            _notificationServiceMock.Object,
            _sweepServiceMock.Object);
    }

    private static UserAccount ActiveStaff(UserRole role, int id = 11) => new()
    {
        Id = id,
        Username = $"staff_{id}",
        Email = $"staff{id}@example.com",
        FullName = "Nguyen Thi Lan",
        Role = role,
        Status = AccountStatus.Active,
        TokenVersion = 1,
        CommitmentAcceptedAt = DateTime.UtcNow.AddDays(-30)
    };

    private static UserAccount PendingStaff(UserRole role, int id = 20) => new()
    {
        Id = id,
        Email = "newstaff@example.com",
        FullName = "Le Van An",
        Expertise = role == UserRole.Moderator ? "Giao vien tieu hoc" : null,
        Role = role,
        Status = AccountStatus.PasswordResetPending,
        ResetTokenHash = "invite-hash",
        ResetTokenExpiresAt = DateTime.UtcNow.AddHours(48)
    };

    private void ReturnTarget(UserAccount target) =>
        _userRepoMock.Setup(r => r.GetByIdAsync(
                target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

    private void ReturnActiveAdministratorCount(int count) =>
        _userRepoMock.Setup(r => r.CountAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(count);

    // ---------- Invite ----------

    [Theory]
    [InlineData(UserRole.Moderator, "mod_", "Giáo viên tiểu học", "Giáo viên tiểu học", "ModeratorInvited")]
    [InlineData(UserRole.Administrator, "admin_", "Bị bỏ qua", null, "AdministratorInvited")]
    public async Task InviteAsync_NewEmail_CreatesPendingAccountForRoleAndSendsInviteEmail(
        UserRole role, string usernamePrefix, string expertise, string? savedExpertise, string auditAction)
    {
        var created = PendingStaff(role);
        created.Expertise = null;
        _provisioningMock
            .Setup(p => p.CreatePendingAccountAsync(
                It.Is<string>(u => u.StartsWith(usernamePrefix) && u.Length <= 50),
                "newstaff@example.com", "Le Van An", null,
                role, TimeSpan.FromHours(72),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((created, "raw-invite-token"));

        var result = await _sut.InviteAsync(_admin.Id, new InviteStaffRequestDto
        {
            Role = role,
            FullName = "Le Van An",
            Email = "  NewStaff@Example.com ",
            Expertise = expertise,
            Note = "Bổ sung nhân sự"
        });

        Assert.Equal(role.ToString(), result.Role);
        Assert.Equal(savedExpertise, created.Expertise);
        Assert.Equal("PasswordResetPending", result.Status);
        Assert.False(result.IsActivated);
        _emailSenderMock.Verify(e => e.SendStaffInviteEmailAsync(
            created.Email, created.FullName, "Admin Demo",
            It.Is<string>(label => label.Contains(role.ToString())), "raw-invite-token",
            It.IsAny<CancellationToken>()), Times.Once);
        _auditLogWriterMock.Verify(a => a.LogAsync(
            _admin.Id, auditAction, nameof(UserAccount), created.Id,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InviteAsync_ModeratorWithoutExpertise_ThrowsBadRequestAndCreatesNothing()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.InviteAsync(
            _admin.Id, new InviteStaffRequestDto
            {
                Role = UserRole.Moderator, FullName = "Le Van An",
                Email = "newstaff@example.com", Expertise = "  "
            }));

        _provisioningMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(UserRole.Parent)]
    [InlineData(UserRole.ContentCreator)]
    [InlineData(null)]
    public async Task InviteAsync_RoleIsNotStaff_ThrowsBadRequestAndCreatesNothing(UserRole? role)
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.InviteAsync(
            _admin.Id, new InviteStaffRequestDto
            {
                Role = role, FullName = "Le Van An",
                Email = "newstaff@example.com", Expertise = "Giáo viên"
            }));

        _provisioningMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InviteAsync_EmailAlreadyUsed_PropagatesBadRequestAndSendsNothing()
    {
        _provisioningMock
            .Setup(p => p.CreatePendingAccountAsync(
                It.IsAny<string>(), "parent1@example.com", It.IsAny<string>(), null,
                UserRole.Moderator, It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BadRequestException("Email 'parent1@example.com' đã được sử dụng trong hệ thống."));

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.InviteAsync(
            _admin.Id, new InviteStaffRequestDto
            {
                Role = UserRole.Moderator, FullName = "Tran Thi Hoa",
                Email = "parent1@example.com", Expertise = "Giáo viên"
            }));

        _emailSenderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InviteAsync_InactiveActor_ThrowsForbidden()
    {
        _admin.Status = AccountStatus.Suspended;

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.InviteAsync(
            _admin.Id, new InviteStaffRequestDto
            {
                Role = UserRole.Administrator, FullName = "New Admin", Email = "newadmin@example.com"
            }));

        _provisioningMock.VerifyNoOtherCalls();
    }

    // ---------- Resend / cancel invite ----------

    [Theory]
    [InlineData(UserRole.Moderator)]
    [InlineData(UserRole.Administrator)]
    public async Task ResendInviteAsync_PendingAccount_IssuesNewTokenValidFor72Hours(UserRole role)
    {
        var pending = PendingStaff(role);
        pending.ResetTokenExpiresAt = DateTime.UtcNow.AddHours(-1);
        ReturnTarget(pending);
        _jwtGeneratorMock.Setup(j => j.GenerateRefreshToken()).Returns("fresh-invite-token");

        await _sut.ResendInviteAsync(_admin.Id, pending.Id);

        Assert.Equal(TokenHasher.Hash("fresh-invite-token"), pending.ResetTokenHash);
        Assert.InRange(
            pending.ResetTokenExpiresAt!.Value,
            DateTime.UtcNow.AddHours(71.9),
            DateTime.UtcNow.AddHours(72.1));
        _emailSenderMock.Verify(e => e.SendStaffInviteEmailAsync(
            pending.Email, pending.FullName, "Admin Demo",
            It.Is<string>(label => label.Contains(role.ToString())),
            "fresh-invite-token", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(UserRole.Moderator)]
    [InlineData(UserRole.Administrator)]
    public async Task ResendInviteAsync_AlreadyActivated_ThrowsConflict(UserRole role)
    {
        var activated = ActiveStaff(role);
        ReturnTarget(activated);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.ResendInviteAsync(_admin.Id, activated.Id));
    }

    [Theory]
    [InlineData(UserRole.Moderator, "ModeratorInviteCancelled")]
    [InlineData(UserRole.Administrator, "AdministratorInviteCancelled")]
    public async Task CancelInviteAsync_PendingAccount_DeletesAccountAndAudits(UserRole role, string auditAction)
    {
        var pending = PendingStaff(role);
        ReturnTarget(pending);

        var result = await _sut.CancelInviteAsync(_admin.Id, pending.Id);

        Assert.Equal(pending.Id, result.UserId);
        Assert.Equal(role.ToString(), result.Role);
        _userRepoMock.Verify(r => r.Delete(pending), Times.Once);
        _unitOfWorkMock.Verify(
            u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditLogWriterMock.Verify(a => a.LogAsync(
            _admin.Id, auditAction, nameof(UserAccount), pending.Id,
            It.IsAny<object?>(), null, It.IsAny<CancellationToken>()), Times.Once);
        _emailSenderMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(UserRole.Moderator)]
    [InlineData(UserRole.Administrator)]
    public async Task CancelInviteAsync_AlreadyActivated_ThrowsConflictAndKeepsAccount(UserRole role)
    {
        var activated = ActiveStaff(role);
        ReturnTarget(activated);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.CancelInviteAsync(_admin.Id, activated.Id));

        _userRepoMock.Verify(r => r.Delete(It.IsAny<UserAccount>()), Times.Never);
    }

    [Theory]
    [InlineData(UserRole.Parent)]
    [InlineData(UserRole.ContentCreator)]
    public async Task ActionsOnUser_TargetIsNotStaff_ThrowNotFound(UserRole role)
    {
        var other = new UserAccount { Id = 5, Role = role, Status = AccountStatus.PasswordResetPending };
        ReturnTarget(other);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ResendInviteAsync(_admin.Id, other.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CancelInviteAsync(_admin.Id, other.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeactivateAsync(_admin.Id, other.Id, "Nghỉ việc"));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ReactivateAsync(_admin.Id, other.Id));

        _userRepoMock.Verify(r => r.Delete(It.IsAny<UserAccount>()), Times.Never);
    }

    // ---------- List ----------

    [Fact]
    public async Task ListAsync_NoRole_ReturnsModeratorsAndAdministratorsNewestFirst()
    {
        var olderModerator = ActiveStaff(UserRole.Moderator, 11);
        olderModerator.CreatedAt = DateTime.UtcNow.AddDays(-3);
        var newerAdmin = ActiveStaff(UserRole.Administrator, 30);
        newerAdmin.CreatedAt = DateTime.UtcNow.AddDays(-1);
        Expression<Func<UserAccount, bool>>? filter = null;
        _userRepoMock.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<UserAccount, bool>>, string?, CancellationToken>((f, _, _) => filter = f)
            .ReturnsAsync(new List<UserAccount> { olderModerator, newerAdmin });

        var result = await _sut.ListAsync(null);

        Assert.Equal(new[] { 30, 11 }, result.Select(item => item.UserId));
        Assert.Equal(new[] { "Administrator", "Moderator" }, result.Select(item => item.Role));
        var compiled = filter!.Compile();
        Assert.True(compiled(ActiveStaff(UserRole.Moderator)));
        Assert.True(compiled(ActiveStaff(UserRole.Administrator)));
        Assert.False(compiled(ActiveStaff(UserRole.Parent)));
        Assert.False(compiled(ActiveStaff(UserRole.ContentCreator)));
    }

    [Theory]
    [InlineData(UserRole.Moderator, UserRole.Administrator)]
    [InlineData(UserRole.Administrator, UserRole.Moderator)]
    public async Task ListAsync_WithRole_FiltersToThatRole(UserRole role, UserRole otherRole)
    {
        Expression<Func<UserAccount, bool>>? filter = null;
        _userRepoMock.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<UserAccount, bool>>, string?, CancellationToken>((f, _, _) => filter = f)
            .ReturnsAsync(new List<UserAccount>());

        await _sut.ListAsync(role);

        var compiled = filter!.Compile();
        Assert.True(compiled(ActiveStaff(role)));
        Assert.False(compiled(ActiveStaff(otherRole)));
    }

    [Fact]
    public async Task ListAsync_RoleIsNotStaff_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ListAsync(UserRole.Parent));
    }

    [Fact]
    public async Task ListAsync_LegacyActiveAdministratorWithoutCommitment_IsActivated()
    {
        var legacyAdmin = ActiveStaff(UserRole.Administrator);
        legacyAdmin.CommitmentAcceptedAt = null;
        _userRepoMock.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserAccount> { legacyAdmin });

        var result = await _sut.ListAsync(UserRole.Administrator);

        Assert.True(Assert.Single(result).IsActivated);
    }

    // ---------- Deactivate ----------

    [Theory]
    [InlineData(UserRole.Moderator, "ModeratorDeactivated")]
    [InlineData(UserRole.Administrator, "AdministratorDeactivated")]
    public async Task DeactivateAsync_ActiveAccount_SuspendsRevokesSessionsReleasesClaimsAndNotifies(
        UserRole role, string auditAction)
    {
        var target = ActiveStaff(role);
        ReturnTarget(target);
        ReturnActiveAdministratorCount(2);
        var session = new RefreshToken { Id = 1, UserAccountId = target.Id, TokenHash = "h" };
        _refreshTokenRepoMock
            .Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<RefreshToken, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RefreshToken> { session });

        var result = await _sut.DeactivateAsync(_admin.Id, target.Id, "Nghỉ việc");

        Assert.Equal(AccountStatus.Suspended, target.Status);
        Assert.Equal(2, target.TokenVersion);
        Assert.NotNull(session.RevokedAt);
        Assert.Equal("Suspended", result.Status);
        _auditLogWriterMock.Verify(a => a.LogAsync(
            _admin.Id, auditAction, nameof(UserAccount), target.Id,
            It.IsAny<object?>(), It.IsAny<object?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _notificationServiceMock.Verify(n => n.CreateAsync(
            target.Id, NotificationType.AdminRoleChanged, It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _sweepServiceMock.Verify(service => service.ReleaseClaimsOfModeratorAsync(
            target.Id, It.IsAny<CancellationToken>()), Times.Once);
        _sweepServiceMock.Verify(service => service.WithdrawRoadmapReviewsOfInactiveCreatorsAsync(
            target.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeactivateAsync_LegacyAdministratorWithoutCommitment_CanBeDeactivated()
    {
        var legacyAdmin = ActiveStaff(UserRole.Administrator);
        legacyAdmin.CommitmentAcceptedAt = null;
        ReturnTarget(legacyAdmin);
        ReturnActiveAdministratorCount(2);

        var result = await _sut.DeactivateAsync(_admin.Id, legacyAdmin.Id, "Nghỉ việc");

        Assert.Equal("Suspended", result.Status);
        Assert.True(result.IsActivated);
    }

    [Fact]
    public async Task DeactivateAsync_Administrator_CountsOnlyActiveAdministrators()
    {
        var target = ActiveStaff(UserRole.Administrator);
        ReturnTarget(target);
        Expression<Func<UserAccount, bool>>? counted = null;
        _userRepoMock.Setup(r => r.CountAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<UserAccount, bool>>?, CancellationToken>((f, _) => counted = f)
            .ReturnsAsync(2);

        await _sut.DeactivateAsync(_admin.Id, target.Id, "Nghỉ việc");

        var compiled = counted!.Compile();
        Assert.True(compiled(ActiveStaff(UserRole.Administrator)));
        Assert.False(compiled(PendingStaff(UserRole.Administrator)));
        Assert.False(compiled(new UserAccount { Role = UserRole.Administrator, Status = AccountStatus.Suspended }));
        Assert.False(compiled(ActiveStaff(UserRole.Moderator)));
    }

    [Fact]
    public async Task DeactivateAsync_LastActiveAdministrator_ThrowsConflict()
    {
        var target = ActiveStaff(UserRole.Administrator);
        ReturnTarget(target);
        ReturnActiveAdministratorCount(1);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.DeactivateAsync(_admin.Id, target.Id, "Nghỉ việc"));

        Assert.Equal(AccountStatus.Active, target.Status);
    }

    [Fact]
    public async Task DeactivateAsync_Moderator_DoesNotCountAdministrators()
    {
        var moderator = ActiveStaff(UserRole.Moderator);
        ReturnTarget(moderator);

        await _sut.DeactivateAsync(_admin.Id, moderator.Id, "Nghỉ việc");

        _userRepoMock.Verify(r => r.CountAsync(
            It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeactivateAsync_Self_ThrowsConflict()
    {
        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.DeactivateAsync(_admin.Id, _admin.Id, "Nghỉ việc"));

        Assert.Equal(AccountStatus.Active, _admin.Status);
    }

    [Theory]
    [InlineData(UserRole.Moderator)]
    [InlineData(UserRole.Administrator)]
    public async Task DeactivateAsync_AlreadySuspended_ThrowsConflictWithoutSweeping(UserRole role)
    {
        var target = ActiveStaff(role);
        target.Status = AccountStatus.Suspended;
        ReturnTarget(target);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.DeactivateAsync(_admin.Id, target.Id, "Nghỉ việc"));

        _sweepServiceMock.Verify(service => service.ReleaseClaimsOfModeratorAsync(
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(UserRole.Moderator)]
    [InlineData(UserRole.Administrator)]
    public async Task DeactivateAsync_NotYetActivated_ThrowsBadRequest(UserRole role)
    {
        var pending = PendingStaff(role);
        ReturnTarget(pending);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.DeactivateAsync(_admin.Id, pending.Id, "Nhầm email"));
    }

    [Fact]
    public async Task DeactivateAsync_BlankReason_ThrowsBadRequest()
    {
        var moderator = ActiveStaff(UserRole.Moderator);
        ReturnTarget(moderator);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.DeactivateAsync(_admin.Id, moderator.Id, "  "));

        Assert.Equal(AccountStatus.Active, moderator.Status);
    }

    // ---------- Reactivate ----------

    [Theory]
    [InlineData(UserRole.Moderator, "ModeratorReactivated")]
    [InlineData(UserRole.Administrator, "AdministratorReactivated")]
    public async Task ReactivateAsync_SuspendedAccount_ReturnsToActiveAndClearsLockout(
        UserRole role, string auditAction)
    {
        var target = ActiveStaff(role);
        target.Status = AccountStatus.Suspended;
        target.FailedLoginAttempts = 3;
        target.LockedUntil = DateTime.UtcNow.AddMinutes(10);
        ReturnTarget(target);

        var result = await _sut.ReactivateAsync(_admin.Id, target.Id);

        Assert.Equal(AccountStatus.Active, target.Status);
        Assert.Equal(0, target.FailedLoginAttempts);
        Assert.Null(target.LockedUntil);
        Assert.Equal("Active", result.Status);
        _auditLogWriterMock.Verify(a => a.LogAsync(
            _admin.Id, auditAction, nameof(UserAccount), target.Id,
            It.IsAny<object?>(), It.IsAny<object?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(UserRole.Moderator)]
    [InlineData(UserRole.Administrator)]
    public async Task ReactivateAsync_NotSuspended_ThrowsConflict(UserRole role)
    {
        var active = ActiveStaff(role);
        ReturnTarget(active);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.ReactivateAsync(_admin.Id, active.Id));
    }

    // ---------- Controller ----------

    [Fact]
    public void StaffAccountController_IsRestrictedToAdministrator()
    {
        var authorize = typeof(StaffAccountController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Administrator", authorize!.Roles);
    }

    [Theory]
    [InlineData(nameof(StaffAccountController.Invite), "invite")]
    [InlineData(nameof(StaffAccountController.ResendInvite), "{userId:int}/resend-invite")]
    [InlineData(nameof(StaffAccountController.CancelInvite), "{userId:int}/cancel-invite")]
    [InlineData(nameof(StaffAccountController.Deactivate), "{userId:int}/deactivate")]
    [InlineData(nameof(StaffAccountController.Reactivate), "{userId:int}/reactivate")]
    public void StaffAccountController_PostRoutes(string action, string template)
    {
        var post = typeof(StaffAccountController).GetMethod(action)!
            .GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>();

        Assert.NotNull(post);
        Assert.Equal(template, post!.Template);
    }
}
