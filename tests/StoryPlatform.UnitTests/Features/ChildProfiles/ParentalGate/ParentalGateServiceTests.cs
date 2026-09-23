using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.ParentalGate.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.ParentalGate.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.ParentalGate;

public class ParentalGateServiceTests
{
    private readonly Mock<IGenericRepository<UserAccount>> _userRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly ParentalGateService _sut;

    public ParentalGateServiceTests()
    {
        _unitOfWork.Setup(u => u.Repository<UserAccount>()).Returns(_userRepo.Object);
        _sut = new ParentalGateService(_unitOfWork.Object, _passwordHasher.Object);
    }

    [Fact]
    public async Task VerifyAsync_UnknownEmail_ThrowsBadRequest()
    {
        SetupUser(null);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.VerifyAsync(
            new VerifyParentalGateRequestDto { Email = "ghost@example.com", Password = "whatever1" }));

        _passwordHasher.Verify(
            hasher => hasher.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task VerifyAsync_AccountLocked_ThrowsForbiddenWithoutCheckingPassword()
    {
        var user = SupervisorUser();
        user.LockedUntil = DateTime.UtcNow.AddMinutes(5);
        SetupUser(user);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.VerifyAsync(
            new VerifyParentalGateRequestDto { Email = user.Email, Password = "whatever1" }));

        _passwordHasher.Verify(
            hasher => hasher.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task VerifyAsync_WrongPassword_IncrementsFailedAttemptsAndThrowsBadRequest()
    {
        var user = SupervisorUser();
        user.FailedLoginAttempts = 2;
        SetupUser(user);
        _passwordHasher.Setup(hasher => hasher.VerifyPassword("wrong-pass", user.PasswordHash))
            .Returns(false);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.VerifyAsync(
            new VerifyParentalGateRequestDto { Email = user.Email, Password = "wrong-pass" }));

        Assert.Equal(3, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
        _userRepo.Verify(repository => repository.Update(user), Times.Once);
        _unitOfWork.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_FifthWrongPassword_LocksAccountFor15Minutes()
    {
        var user = SupervisorUser();
        user.FailedLoginAttempts = 4;
        SetupUser(user);
        _passwordHasher.Setup(hasher => hasher.VerifyPassword("wrong-pass", user.PasswordHash))
            .Returns(false);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.VerifyAsync(
            new VerifyParentalGateRequestDto { Email = user.Email, Password = "wrong-pass" }));

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.True(user.LockedUntil > DateTime.UtcNow.AddMinutes(14));
    }

    [Fact]
    public async Task VerifyAsync_SuspendedAccount_ThrowsForbidden()
    {
        var user = SupervisorUser();
        user.Status = AccountStatus.Suspended;
        SetupUser(user);
        _passwordHasher.Setup(hasher => hasher.VerifyPassword("correct-pass", user.PasswordHash))
            .Returns(true);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.VerifyAsync(
            new VerifyParentalGateRequestDto { Email = user.Email, Password = "correct-pass" }));
    }

    [Fact]
    public async Task VerifyAsync_CorrectPassword_ResetsFailedAttemptsAndCompletesSuccessfully()
    {
        var user = SupervisorUser();
        user.FailedLoginAttempts = 3;
        user.Status = AccountStatus.LoggedOut;
        user.LastLoginAt = DateTime.UtcNow.AddDays(-1);
        var previousLastLoginAt = user.LastLoginAt;
        SetupUser(user);
        _passwordHasher.Setup(hasher => hasher.VerifyPassword("correct-pass", user.PasswordHash))
            .Returns(true);

        await _sut.VerifyAsync(
            new VerifyParentalGateRequestDto { Email = user.Email, Password = "correct-pass" });

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
        Assert.Equal(AccountStatus.LoggedOut, user.Status);
        Assert.Equal(previousLastLoginAt, user.LastLoginAt);
        _userRepo.Verify(repository => repository.Update(user), Times.Once);
        _unitOfWork.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private void SetupUser(UserAccount? user) => _userRepo
        .Setup(repository => repository.FirstOrDefaultAsync(
            It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(user);

    private static UserAccount SupervisorUser() => new()
    {
        Id = 7,
        Email = "parent@example.com",
        Username = "parent",
        FullName = "Chị An",
        PasswordHash = "hashed-real-password",
        Role = UserRole.Parent,
        Status = AccountStatus.LoggedIn
    };
}
