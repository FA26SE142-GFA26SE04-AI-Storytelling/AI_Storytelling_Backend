using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Auth.DTOs;
using StoryPlatform.Application.Features.Auth.Services;
using StoryPlatform.Application.Features.ChildProfiles.Sessions.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Auth;

public class AccountDeletionServiceTests
{
    private readonly Mock<IGenericRepository<UserAccount>> _userRepo = new();
    private readonly Mock<IGenericRepository<ChildProfile>> _profileRepo = new();
    private readonly Mock<IGenericRepository<SupervisionRelationship>> _relationshipRepo = new();
    private readonly Mock<IGenericRepository<PaymentTransaction>> _paymentRepo = new();
    private readonly Mock<IGenericRepository<RefreshToken>> _refreshRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<ISupervisionService> _supervisionService = new();
    private readonly Mock<IChildSessionRevoker> _childSessionRevoker = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly Mock<IAuditLogWriter> _auditLogWriter = new();
    private readonly AccountDeletionService _sut;

    public AccountDeletionServiceTests()
    {
        _unitOfWork.Setup(u => u.Repository<UserAccount>()).Returns(_userRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ChildProfile>()).Returns(_profileRepo.Object);
        _unitOfWork.Setup(u => u.Repository<SupervisionRelationship>()).Returns(_relationshipRepo.Object);
        _unitOfWork.Setup(u => u.Repository<PaymentTransaction>()).Returns(_paymentRepo.Object);
        _unitOfWork.Setup(u => u.Repository<RefreshToken>()).Returns(_refreshRepo.Object);
        _profileRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<ChildProfile, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChildProfile>());
        _relationshipRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupervisionRelationship>());
        _paymentRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<PaymentTransaction, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentTransaction>());
        _refreshRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<RefreshToken, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RefreshToken>());
        _passwordHasher.Setup(p => p.VerifyPassword("Pass@123", "stored-hash"))
            .Returns(true);
        _passwordHasher.Setup(p => p.HashPassword(It.IsAny<string>()))
            .Returns("random-hash");
        _sut = new AccountDeletionService(
            _unitOfWork.Object, _passwordHasher.Object, _supervisionService.Object,
            _childSessionRevoker.Object, _notificationService.Object,
            _emailSender.Object, _auditLogWriter.Object,
            NullLogger<AccountDeletionService>.Instance);
    }

    [Fact]
    public async Task DeleteAccountAsync_NonParent_ThrowsForbidden()
    {
        SetupUser(UserRole.Administrator);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.DeleteAccountAsync(1, new DeleteAccountRequestDto
            {
                CurrentPassword = "Pass@123"
            }));
    }

    [Fact]
    public async Task DeleteAccountAsync_WrongPassword_ThrowsBeforeTransaction()
    {
        var user = SetupUser();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.DeleteAccountAsync(1, new DeleteAccountRequestDto
            {
                CurrentPassword = "wrong"
            }));

        Assert.False(user.IsDeleted);
        _unitOfWork.Verify(
            u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAccountAsync_OwnerOfActiveProfile_ThrowsBadRequest()
    {
        var user = SetupUser();
        SetupOwnedProfiles(new ChildProfile
        {
            Id = 5,
            OwnerUserId = 1,
            Nickname = "Bé An",
            Status = ChildProfileStatus.Active
        });

        var exception = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.DeleteAccountAsync(1, new DeleteAccountRequestDto
            {
                CurrentPassword = "Pass@123"
            }));

        Assert.Contains("Bé An", exception.Message);
        Assert.False(user.IsDeleted);
    }

    [Fact]
    public async Task DeleteAccountAsync_DraftProfilesWithoutConfirmation_ThrowsBadRequest()
    {
        SetupUser();
        SetupOwnedProfiles(new ChildProfile
        {
            Id = 6,
            OwnerUserId = 1,
            Nickname = "Nháp",
            Status = ChildProfileStatus.Draft
        });

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.DeleteAccountAsync(1, new DeleteAccountRequestDto
            {
                CurrentPassword = "Pass@123",
                DeleteDraftProfiles = false
            }));
    }

    [Fact]
    public async Task GetPreviewAsync_SplitsBlockingAndDraftProfiles()
    {
        SetupUser();
        SetupOwnedProfiles(
            new ChildProfile
            {
                Id = 5, OwnerUserId = 1, Nickname = "A",
                Status = ChildProfileStatus.Suspended
            },
            new ChildProfile
            {
                Id = 6, OwnerUserId = 1, Nickname = "B",
                Status = ChildProfileStatus.Draft
            });

        var preview = await _sut.GetPreviewAsync(1);

        Assert.Equal(new[] { 5 }, preview.BlockingProfiles.Select(p => p.Id));
        Assert.Equal(new[] { 6 }, preview.DraftProfiles.Select(p => p.Id));
        Assert.False(preview.CanDelete);
    }

    [Fact]
    public async Task DeleteAccountAsync_Valid_AnonymizesRevokesAndNotifiesInOneTransaction()
    {
        var user = SetupUser();
        var draft = new ChildProfile
        {
            Id = 6, OwnerUserId = 1, Nickname = "Nháp",
            Status = ChildProfileStatus.Draft
        };
        SetupOwnedProfiles(draft);
        var supervised = new SupervisionRelationship
        {
            Id = 20,
            ChildProfileId = 9,
            SupervisorUserId = 1,
            SupervisorRole = SupervisorRole.AdditionalSupervisor
        };
        _relationshipRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupervisionRelationship> { supervised });
        _profileRepo.Setup(r => r.GetByIdAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile
            {
                Id = 9,
                OwnerUserId = 3,
                Nickname = "Con người khác"
            });
        var pending = new PaymentTransaction
        {
            Id = 70,
            PayerUserId = 1,
            Status = PaymentStatus.Pending
        };
        _paymentRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<PaymentTransaction, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentTransaction> { pending });
        var token = new RefreshToken { Id = 1, UserAccountId = 1 };
        _refreshRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<RefreshToken, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RefreshToken> { token });

        await _sut.DeleteAccountAsync(1, new DeleteAccountRequestDto
        {
            CurrentPassword = "Pass@123",
            DeleteDraftProfiles = true
        });

        Assert.Equal(ChildProfileStatus.Deleted, draft.Status);
        Assert.Equal(PaymentStatus.Expired, pending.Status);
        Assert.NotNull(token.RevokedAt);
        _supervisionService.Verify(s => s.ApplyRevocationAsync(
            supervised, 1, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _childSessionRevoker.Verify(r => r.RevokeBySupervisorAsync(
            1, null, It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(user.IsDeleted);
        Assert.StartsWith("deleted-", user.Email);
        Assert.StartsWith("deleted-", user.Username);
        Assert.Null(user.AvatarUrl);
        Assert.Equal("random-hash", user.PasswordHash);
        _unitOfWork.Verify(
            u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _notificationService.Verify(n => n.CreateAsync(
            3, NotificationType.SupervisionRevoked, It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _emailSender.Verify(e => e.SendAccountSecurityChangedEmailAsync(
            "parent@example.com", "Nguyen Van A", AccountSecurityChange.AccountDeleted,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAccountAsync_SaveFails_RollsBackWithoutEmailOrNotification()
    {
        SetupUser();
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.DeleteAccountAsync(1, new DeleteAccountRequestDto
            {
                CurrentPassword = "Pass@123"
            }));

        _unitOfWork.Verify(
            u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(
            u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _emailSender.Verify(e => e.SendAccountSecurityChangedEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AccountSecurityChange>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private UserAccount SetupUser(UserRole role = UserRole.Parent)
    {
        var user = new UserAccount
        {
            Id = 1,
            Username = "parent_a",
            Email = "parent@example.com",
            FullName = "Nguyen Van A",
            PasswordHash = "stored-hash",
            Role = role,
            Status = AccountStatus.Active,
            AvatarUrl = "https://cdn.example.com/a.png"
        };
        _userRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        return user;
    }

    private void SetupOwnedProfiles(params ChildProfile[] profiles) =>
        _profileRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<ChildProfile, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(profiles.ToList());
}
