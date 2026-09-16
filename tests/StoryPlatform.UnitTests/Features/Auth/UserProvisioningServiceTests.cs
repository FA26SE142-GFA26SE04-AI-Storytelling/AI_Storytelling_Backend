using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Security;
using StoryPlatform.Application.Features.Auth.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Auth;

public class UserProvisioningServiceTests
{
    private readonly Mock<IGenericRepository<UserAccount>> _userRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IJwtTokenGenerator> _jwtGeneratorMock = new();
    private readonly UserProvisioningService _sut;

    public UserProvisioningServiceTests()
    {
        _unitOfWorkMock.Setup(u => u.Repository<UserAccount>()).Returns(_userRepoMock.Object);
        _jwtGeneratorMock.SetupSequence(j => j.GenerateRefreshToken())
            .Returns("raw-initial-password")
            .Returns("raw-set-password-token");
        _passwordHasherMock.Setup(p => p.HashPassword(It.IsAny<string>())).Returns("hashed-random-password");
        _sut = new UserProvisioningService(
            _unitOfWorkMock.Object, _passwordHasherMock.Object, _jwtGeneratorMock.Object);
    }

    [Fact]
    public async Task CreatePendingAccountAsync_NewEmailAndUsername_CreatesAccountPendingPasswordSetup()
    {
        _userRepoMock.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        UserAccount? added = null;
        _userRepoMock
            .Setup(r => r.AddAsync(It.IsAny<UserAccount>(), It.IsAny<CancellationToken>()))
            .Callback<UserAccount, CancellationToken>((u, _) => added = u)
            .ReturnsAsync((UserAccount u, CancellationToken _) => u);

        var (account, rawSetPasswordToken) = await _sut.CreatePendingAccountAsync(
            "teacher1", "teacher1@example.com", "Le Thi B", "0900000000", UserRole.Teacher);

        Assert.NotNull(added);
        Assert.Same(added, account);
        Assert.Equal("teacher1", account.Username);
        Assert.Equal("teacher1@example.com", account.Email);
        Assert.Equal(UserRole.Teacher, account.Role);
        Assert.Equal(AccountStatus.PasswordResetPending, account.Status);
        Assert.Equal("hashed-random-password", account.PasswordHash);
        Assert.Equal(TokenHasher.Hash("raw-set-password-token"), account.ResetTokenHash);
        Assert.True(account.ResetTokenExpiresAt > DateTime.UtcNow.AddDays(6));
        Assert.Equal("raw-set-password-token", rawSetPasswordToken);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreatePendingAccountAsync_EmailAlreadyUsed_ThrowsBadRequestException()
    {
        _userRepoMock.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreatePendingAccountAsync(
            "teacher1", "taken@example.com", "Le Thi B", null, UserRole.Teacher));

        _userRepoMock.Verify(
            r => r.AddAsync(It.IsAny<UserAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
