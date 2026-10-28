using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Security;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Auth.DTOs;
using StoryPlatform.Application.Features.Auth.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Auth;

public class StaffActivationServiceTests
{
    private const string RawToken = "raw-invite-token";

    private readonly Mock<IGenericRepository<UserAccount>> _userRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IAuditLogWriter> _auditLogWriterMock = new();
    private readonly StaffActivationService _sut;
    private readonly UserAccount _invitee;

    public StaffActivationServiceTests()
    {
        _invitee = new UserAccount
        {
            Id = 20,
            Username = "mod_x",
            Email = "newmod@example.com",
            FullName = "Le Van An",
            Role = UserRole.Moderator,
            Status = AccountStatus.PasswordResetPending,
            PasswordHash = "random-discarded",
            TokenVersion = 1,
            ResetTokenHash = TokenHasher.Hash(RawToken),
            ResetTokenExpiresAt = DateTime.UtcNow.AddHours(70)
        };
        _unitOfWorkMock.Setup(u => u.Repository<UserAccount>())
            .Returns(_userRepoMock.Object);
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_invitee);
        _passwordHasherMock.Setup(p => p.HashPassword("Matkhau123"))
            .Returns("hashed-new-password");
        _sut = new StaffActivationService(
            _unitOfWorkMock.Object,
            _passwordHasherMock.Object,
            _auditLogWriterMock.Object);
    }

    private static StaffActivationRequestDto Request(
        string token = RawToken, bool commitment = true) => new()
    {
        Email = "newmod@example.com",
        InviteToken = token,
        NewPassword = "Matkhau123",
        ConfirmPassword = "Matkhau123",
        CommitmentAccepted = commitment
    };

    [Fact]
    public async Task ActivateAsync_ValidInvite_ActivatesAccountAndBurnsInviteToken()
    {
        await _sut.ActivateAsync(Request());

        Assert.Equal(AccountStatus.Active, _invitee.Status);
        Assert.Equal("hashed-new-password", _invitee.PasswordHash);
        Assert.NotNull(_invitee.CommitmentAcceptedAt);
        Assert.NotNull(_invitee.EmailVerifiedAt);
        Assert.Null(_invitee.ResetTokenHash);
        Assert.Null(_invitee.ResetTokenExpiresAt);
        Assert.Equal(2, _invitee.TokenVersion);
        _auditLogWriterMock.Verify(a => a.LogAsync(
            _invitee.Id, "ModeratorActivated", nameof(UserAccount), _invitee.Id,
            It.IsAny<object?>(), It.IsAny<object?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ActivateAsync_CommitmentNotAccepted_ThrowsAndLeavesAccountPending()
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ActivateAsync(Request(commitment: false)));

        Assert.Equal(AccountStatus.PasswordResetPending, _invitee.Status);
        Assert.NotNull(_invitee.ResetTokenHash);
    }

    [Fact]
    public async Task ActivateAsync_PasswordConfirmationMismatch_ThrowsBadRequest()
    {
        var request = Request();
        request.ConfirmPassword = "Khac12345";

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ActivateAsync(request));

        Assert.Equal(AccountStatus.PasswordResetPending, _invitee.Status);
    }

    [Fact]
    public async Task ActivateAsync_WrongToken_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ActivateAsync(Request(token: "wrong")));

        Assert.Equal(AccountStatus.PasswordResetPending, _invitee.Status);
    }

    [Fact]
    public async Task ActivateAsync_ExpiredToken_ThrowsBadRequest()
    {
        _invitee.ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(-1);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ActivateAsync(Request()));
    }

    [Fact]
    public async Task ActivateAsync_ReusingTokenAfterActivation_ThrowsBadRequest()
    {
        _invitee.Status = AccountStatus.Active;
        _invitee.ResetTokenHash = null;

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ActivateAsync(Request()));
    }

    [Fact]
    public async Task ActivateAsync_PendingAdministrator_ActivatesWithAdministratorAudit()
    {
        _invitee.Role = UserRole.Administrator;

        await _sut.ActivateAsync(Request());

        Assert.Equal(AccountStatus.Active, _invitee.Status);
        _auditLogWriterMock.Verify(a => a.LogAsync(
            _invitee.Id, "AdministratorActivated", nameof(UserAccount), _invitee.Id,
            It.IsAny<object?>(), It.IsAny<object?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ActivateAsync_ParentAccount_ThrowsBadRequest()
    {
        _invitee.Role = UserRole.Parent;

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ActivateAsync(Request()));
    }

    [Fact]
    public void ActivationEndpoint_IsAnonymous()
    {
        var method = typeof(AuthController).GetMethod("ActivateStaffAccount");

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<AllowAnonymousAttribute>());
    }
}
