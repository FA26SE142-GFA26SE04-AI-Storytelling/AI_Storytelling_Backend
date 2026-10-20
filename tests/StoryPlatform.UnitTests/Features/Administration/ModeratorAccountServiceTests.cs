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

public class ModeratorAccountServiceTests
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
    private readonly ModeratorAccountService _sut;

    private readonly UserAccount _admin = new()
    {
        Id = 1,
        Username = "admin_demo",
        Email = "admin@example.com",
        FullName = "Admin Demo",
        Role = UserRole.Administrator,
        Status = AccountStatus.Active
    };

    public ModeratorAccountServiceTests()
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
        _sut = new ModeratorAccountService(
            _unitOfWorkMock.Object,
            _provisioningMock.Object,
            _jwtGeneratorMock.Object,
            _emailSenderMock.Object,
            _auditLogWriterMock.Object,
            _notificationServiceMock.Object,
            _sweepServiceMock.Object);
    }

    private static UserAccount ActiveModerator(int id = 11) => new()
    {
        Id = id,
        Username = $"moderator_demo{id}",
        Email = $"moderator{id}@example.com",
        FullName = "Nguyen Thi Lan",
        Role = UserRole.Moderator,
        Status = AccountStatus.Active,
        TokenVersion = 1,
        CommitmentAcceptedAt = DateTime.UtcNow.AddDays(-30)
    };

    private void ReturnTarget(UserAccount target) =>
        _userRepoMock.Setup(r => r.GetByIdAsync(
                target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

    [Fact]
    public async Task InviteAsync_NewEmail_CreatesPendingModeratorAndSendsInviteEmail()
    {
        var created = new UserAccount
        {
            Id = 20,
            Username = "mod_x",
            Email = "newmod@example.com",
            FullName = "Le Van An",
            Role = UserRole.Moderator,
            Status = AccountStatus.PasswordResetPending,
            ResetTokenExpiresAt = DateTime.UtcNow.AddHours(72)
        };
        _provisioningMock
            .Setup(p => p.CreatePendingAccountAsync(
                It.Is<string>(u => u.StartsWith("mod_") && u.Length <= 50),
                "newmod@example.com", "Le Van An", null,
                UserRole.Moderator, TimeSpan.FromHours(72),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((created, "raw-invite-token"));

        var result = await _sut.InviteAsync(
            _admin.Id,
            new InviteModeratorRequestDto
            {
                FullName = "Le Van An",
                Email = "  NewMod@Example.com ",
                Expertise = "Giáo viên tiểu học",
                Note = "Giới thiệu bởi hiệu trưởng"
            });

        Assert.Equal("Giáo viên tiểu học", created.Expertise);
        Assert.Equal("PasswordResetPending", result.Status);
        Assert.False(result.IsActivated);
        _emailSenderMock.Verify(e => e.SendStaffInviteEmailAsync(
            "newmod@example.com", "Le Van An", "Admin Demo",
            It.IsAny<string>(), "raw-invite-token",
            It.IsAny<CancellationToken>()), Times.Once);
        _auditLogWriterMock.Verify(a => a.LogAsync(
            _admin.Id, "ModeratorInvited", nameof(UserAccount), created.Id,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InviteAsync_EmailAlreadyUsed_PropagatesBadRequestAndSendsNothing()
    {
        _provisioningMock
            .Setup(p => p.CreatePendingAccountAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), null,
                UserRole.Moderator, It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BadRequestException("Email đã được sử dụng trong hệ thống."));

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.InviteAsync(
                _admin.Id,
                new InviteModeratorRequestDto
                {
                    FullName = "A",
                    Email = "parent@example.com",
                    Expertise = "x"
                }));

        _emailSenderMock.Verify(e => e.SendStaffInviteEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResendInviteAsync_PendingModerator_IssuesNewTokenValidFor72Hours()
    {
        var pending = new UserAccount
        {
            Id = 20,
            Email = "newmod@example.com",
            FullName = "Le Van An",
            Role = UserRole.Moderator,
            Status = AccountStatus.PasswordResetPending,
            ResetTokenHash = "old",
            ResetTokenExpiresAt = DateTime.UtcNow.AddHours(-1)
        };
        ReturnTarget(pending);
        _jwtGeneratorMock.Setup(j => j.GenerateRefreshToken())
            .Returns("fresh-invite-token");

        await _sut.ResendInviteAsync(_admin.Id, pending.Id);

        Assert.Equal(TokenHasher.Hash("fresh-invite-token"), pending.ResetTokenHash);
        Assert.InRange(
            pending.ResetTokenExpiresAt!.Value,
            DateTime.UtcNow.AddHours(71.9),
            DateTime.UtcNow.AddHours(72.1));
        _emailSenderMock.Verify(e => e.SendStaffInviteEmailAsync(
            pending.Email, pending.FullName, "Admin Demo", It.IsAny<string>(),
            "fresh-invite-token", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResendInviteAsync_AlreadyActivated_ThrowsConflict()
    {
        var activated = ActiveModerator();
        ReturnTarget(activated);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.ResendInviteAsync(_admin.Id, activated.Id));
    }

    [Fact]
    public async Task ResendInviteAsync_TargetIsNotModerator_ThrowsNotFound()
    {
        var parent = new UserAccount
        {
            Id = 5,
            Role = UserRole.Parent,
            Status = AccountStatus.Active
        };
        ReturnTarget(parent);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.ResendInviteAsync(_admin.Id, parent.Id));
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyModeratorsOrderedNewestFirst()
    {
        var older = ActiveModerator(11);
        older.CreatedAt = DateTime.UtcNow.AddDays(-2);
        var newer = ActiveModerator(12);
        newer.CreatedAt = DateTime.UtcNow.AddDays(-1);
        _userRepoMock.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserAccount> { older, newer });

        var result = await _sut.ListAsync();

        Assert.Equal(new[] { 12, 11 }, result.Select(item => item.UserId));
    }

    [Fact]
    public void ModeratorAccountController_IsRestrictedToAdministrator()
    {
        var authorize = typeof(ModeratorAccountController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Administrator", authorize!.Roles);
    }

    [Fact]
    public async Task DeactivateAsync_ActiveModerator_SuspendsRevokesSessionsAndNotifies()
    {
        var moderator = ActiveModerator();
        ReturnTarget(moderator);
        var session = new RefreshToken
        {
            Id = 1,
            UserAccountId = moderator.Id,
            TokenHash = "h",
            SessionScope = SessionScope.Moderator
        };
        _refreshTokenRepoMock
            .Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<RefreshToken, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RefreshToken> { session });

        var result = await _sut.DeactivateAsync(
            _admin.Id, moderator.Id, "Nghỉ việc");

        Assert.Equal(AccountStatus.Suspended, moderator.Status);
        Assert.Equal(2, moderator.TokenVersion);
        Assert.NotNull(session.RevokedAt);
        Assert.Equal("Suspended", result.Status);
        _auditLogWriterMock.Verify(a => a.LogAsync(
            _admin.Id, "ModeratorDeactivated", nameof(UserAccount), moderator.Id,
            It.IsAny<object?>(), It.IsAny<object?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _notificationServiceMock.Verify(n => n.CreateAsync(
            moderator.Id, NotificationType.AdminRoleChanged, It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeactivateAsync_AlreadySuspended_ThrowsConflict()
    {
        var moderator = ActiveModerator();
        moderator.Status = AccountStatus.Suspended;
        ReturnTarget(moderator);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.DeactivateAsync(_admin.Id, moderator.Id, "Nghỉ việc"));

        _sweepServiceMock.Verify(service => service.ReleaseClaimsOfModeratorAsync(
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _sweepServiceMock.Verify(service => service.WithdrawRoadmapReviewsOfInactiveCreatorsAsync(
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeactivateAsync_ReleasesClaimsAndWithdrawsPendingRoadmapReviews()
    {
        var moderator = ActiveModerator();
        ReturnTarget(moderator);

        await _sut.DeactivateAsync(_admin.Id, moderator.Id, "Nghỉ việc");

        _sweepServiceMock.Verify(service => service.ReleaseClaimsOfModeratorAsync(
            moderator.Id, It.IsAny<CancellationToken>()), Times.Once);
        _sweepServiceMock.Verify(service => service.WithdrawRoadmapReviewsOfInactiveCreatorsAsync(
            moderator.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeactivateAsync_ModeratorNeverActivated_ThrowsBadRequest()
    {
        var pending = new UserAccount
        {
            Id = 20,
            Role = UserRole.Moderator,
            Status = AccountStatus.PasswordResetPending
        };
        ReturnTarget(pending);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.DeactivateAsync(_admin.Id, pending.Id, "x lý do"));
    }

    [Fact]
    public async Task DeactivateAsync_BlankReason_ThrowsBadRequest()
    {
        var moderator = ActiveModerator();
        ReturnTarget(moderator);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.DeactivateAsync(_admin.Id, moderator.Id, "  "));

        Assert.Equal(AccountStatus.Active, moderator.Status);
    }

    [Fact]
    public async Task ReactivateAsync_SuspendedModerator_ReturnsToActiveAndClearsLockout()
    {
        var moderator = ActiveModerator();
        moderator.Status = AccountStatus.Suspended;
        moderator.FailedLoginAttempts = 3;
        moderator.LockedUntil = DateTime.UtcNow.AddMinutes(10);
        ReturnTarget(moderator);

        var result = await _sut.ReactivateAsync(_admin.Id, moderator.Id);

        Assert.Equal(AccountStatus.Active, moderator.Status);
        Assert.Equal(0, moderator.FailedLoginAttempts);
        Assert.Null(moderator.LockedUntil);
        Assert.Equal("Active", result.Status);
        _auditLogWriterMock.Verify(a => a.LogAsync(
            _admin.Id, "ModeratorReactivated", nameof(UserAccount), moderator.Id,
            It.IsAny<object?>(), It.IsAny<object?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReactivateAsync_ModeratorNotSuspended_ThrowsConflict()
    {
        var moderator = ActiveModerator();
        ReturnTarget(moderator);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.ReactivateAsync(_admin.Id, moderator.Id));
    }
}
