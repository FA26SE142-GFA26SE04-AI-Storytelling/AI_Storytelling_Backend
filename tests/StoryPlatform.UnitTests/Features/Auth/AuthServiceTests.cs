using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Security;
using StoryPlatform.Application.Features.Auth.DTOs;
using StoryPlatform.Application.Features.Auth.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Auth;

public class AuthServiceTests
{
    private readonly Mock<IGenericRepository<UserAccount>> _userRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IJwtTokenGenerator> _jwtGeneratorMock = new();
    private readonly Mock<IEmailSender> _emailSenderMock = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _unitOfWorkMock.Setup(u => u.Repository<UserAccount>()).Returns(_userRepoMock.Object);
        _jwtGeneratorMock.Setup(j => j.GenerateAccessToken(It.IsAny<UserAccount>())).Returns("fake-access-token");
        _jwtGeneratorMock.Setup(j => j.GenerateRefreshToken()).Returns("fake-refresh-token-raw");
        _jwtGeneratorMock.Setup(j => j.GetExpirationDate()).Returns(DateTime.UtcNow.AddMinutes(120));
        _jwtGeneratorMock.Setup(j => j.GetRefreshTokenExpirationDate()).Returns(DateTime.UtcNow.AddDays(7));
        _jwtGeneratorMock.Setup(j => j.ExpiresInSeconds).Returns(7200L);

        _sut = new AuthService(_unitOfWorkMock.Object, _passwordHasherMock.Object, _jwtGeneratorMock.Object, _emailSenderMock.Object);
    }

    private static UserAccount CreateUser(string password = "hashed-password") => new()
    {
        Id = 1,
        Username = "parent_demo1",
        Email = "parent1@example.com",
        PasswordHash = password,
        FullName = "Tran Thi Hoa",
        Role = UserRole.Parent,
        Status = AccountStatus.EmailVerified
    };

    [Fact]
    public async Task LoginAsync_ValidCredentials_StoresHashedRefreshTokenOnUser()
    {
        var user = CreateUser();
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword("Demo@123", user.PasswordHash)).Returns(true);

        var result = await _sut.LoginAsync(new LoginRequestDto { Identifier = "parent1@example.com", Password = "Demo@123" });

        Assert.Equal("fake-access-token", result.AccessToken);
        Assert.Equal("fake-refresh-token-raw", result.RefreshToken);
        Assert.NotNull(user.RefreshTokenHash);
        Assert.NotEqual("fake-refresh-token-raw", user.RefreshTokenHash);
        Assert.True(user.RefreshTokenExpiresAt > DateTime.UtcNow);
        _userRepoMock.Verify(r => r.Update(user), Times.AtLeastOnce);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsBadRequestException()
    {
        var user = CreateUser();
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.LoginAsync(new LoginRequestDto { Identifier = "parent1@example.com", Password = "WrongPass" }));
    }

    [Fact]
    public async Task LoginAsync_SuspendedAccount_ThrowsForbiddenException()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Suspended;
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.LoginAsync(new LoginRequestDto { Identifier = "parent1@example.com", Password = "Demo@123" }));
    }

    [Fact]
    public async Task RegisterAsync_NewEmail_CreatesUserAndSendsVerificationEmail()
    {
        _userRepoMock.Setup(r => r.ExistsAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _passwordHasherMock.Setup(p => p.HashPassword(It.IsAny<string>())).Returns("hashed-password");
        UserAccount? addedUser = null;
        _userRepoMock
            .Setup(r => r.AddAsync(It.IsAny<UserAccount>(), It.IsAny<CancellationToken>()))
            .Callback<UserAccount, CancellationToken>((u, _) => addedUser = u)
            .ReturnsAsync((UserAccount u, CancellationToken _) => u);

        var request = new RegisterRequestDto
        {
            Username = "parent_new",
            Email = "new@example.com",
            FullName = "Nguoi Dung Moi",
            Password = "Demo@123",
            ConfirmPassword = "Demo@123"
        };

        await _sut.RegisterAsync(request);

        Assert.NotNull(addedUser);
        Assert.Equal(AccountStatus.Registered, addedUser!.Status);
        Assert.Equal(TokenHasher.Hash("fake-refresh-token-raw"), addedUser.EmailVerificationTokenHash);
        Assert.True(addedUser.EmailVerificationTokenExpiresAt > DateTime.UtcNow.AddHours(23));
        // Đăng ký không còn cấp phiên đăng nhập ngay — phải xác thực email trước.
        Assert.Null(addedUser.RefreshTokenHash);
        _emailSenderMock.Verify(e => e.SendEmailVerificationEmailAsync(
            "new@example.com", "Nguoi Dung Moi", "fake-refresh-token-raw", It.IsAny<CancellationToken>()), Times.Once);
        // Tài khoản mới (Id == 0) đang ở trạng thái Added của EF — không được gọi Update().
        _userRepoMock.Verify(r => r.Update(It.IsAny<UserAccount>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_PasswordMismatch_ThrowsBadRequestException()
    {
        var request = new RegisterRequestDto
        {
            Username = "parent_mismatch",
            Email = "mismatch@example.com",
            FullName = "Mismatch User",
            Password = "Password@123",
            ConfirmPassword = "DifferentPassword@123"
        };

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.RegisterAsync(request));
    }

    [Fact]
    public async Task RefreshTokenAsync_ValidToken_RotatesRefreshTokenAndReturnsNewAccessToken()
    {
        var user = CreateUser();
        user.RefreshTokenHash = TokenHasher.Hash("old-raw-refresh-token");
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(1);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _sut.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "old-raw-refresh-token" });

        Assert.Equal("fake-access-token", result.AccessToken);
        Assert.Equal("fake-refresh-token-raw", result.RefreshToken);
        Assert.Equal(TokenHasher.Hash("fake-refresh-token-raw"), user.RefreshTokenHash);
    }

    [Fact]
    public async Task RefreshTokenAsync_UnknownToken_ThrowsUnauthorizedException()
    {
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAccount?)null);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _sut.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "unknown-token" }));
    }

    [Fact]
    public async Task RefreshTokenAsync_ExpiredToken_ThrowsUnauthorizedException()
    {
        var user = CreateUser();
        user.RefreshTokenHash = TokenHasher.Hash("expired-raw-token");
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(-1);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _sut.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "expired-raw-token" }));
    }

    [Fact]
    public async Task LogoutAsync_ClearsRefreshTokenAndSetsLoggedOutStatus()
    {
        var user = CreateUser();
        user.RefreshTokenHash = "some-hash";
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(1);
        user.Status = AccountStatus.LoggedIn;

        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await _sut.LogoutAsync(1);

        Assert.Null(user.RefreshTokenHash);
        Assert.Null(user.RefreshTokenExpiresAt);
        Assert.Equal(AccountStatus.LoggedOut, user.Status);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_UnknownUserId_ThrowsNotFoundException()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((UserAccount?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.LogoutAsync(999));
    }

    [Fact]
    public async Task ForgotPasswordAsync_ExistingEmail_StoresHashedResetTokenAndSendsEmail()
    {
        var user = CreateUser();
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await _sut.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = "parent1@example.com" });

        Assert.NotNull(user.ResetTokenHash);
        Assert.True(user.ResetTokenExpiresAt > DateTime.UtcNow);
        _emailSenderMock.Verify(e => e.SendPasswordResetEmailAsync(
            user.Email, user.FullName, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ForgotPasswordAsync_UnknownEmail_DoesNotThrowAndDoesNotSendEmail()
    {
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAccount?)null);

        await _sut.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = "khong-ton-tai@example.com" });

        _emailSenderMock.Verify(e => e.SendPasswordResetEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidToken_UpdatesPasswordAndClearsTokensAndSessions()
    {
        var user = CreateUser();
        user.ResetTokenHash = TokenHasher.Hash("valid-raw-reset-token");
        user.ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(30);
        user.RefreshTokenHash = "some-old-refresh-hash";
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(1);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.HashPassword("NewPass@123")).Returns("new-hashed-password");

        await _sut.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = user.Email,
            ResetToken = "valid-raw-reset-token",
            NewPassword = "NewPass@123",
            ConfirmPassword = "NewPass@123"
        });

        Assert.Equal("new-hashed-password", user.PasswordHash);
        Assert.Null(user.ResetTokenHash);
        Assert.Null(user.ResetTokenExpiresAt);
        Assert.Null(user.RefreshTokenHash);
        Assert.Null(user.RefreshTokenExpiresAt);
        Assert.Equal(AccountStatus.LoggedOut, user.Status);
    }

    [Fact]
    public async Task ResetPasswordAsync_InvalidToken_ThrowsBadRequestException()
    {
        var user = CreateUser();
        user.ResetTokenHash = TokenHasher.Hash("correct-token");
        user.ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(30);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = user.Email,
            ResetToken = "wrong-token",
            NewPassword = "NewPass@123",
            ConfirmPassword = "NewPass@123"
        }));
    }

    [Fact]
    public async Task ResetPasswordAsync_ExpiredToken_ThrowsBadRequestException()
    {
        var user = CreateUser();
        user.ResetTokenHash = TokenHasher.Hash("expired-token");
        user.ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(-5);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = user.Email,
            ResetToken = "expired-token",
            NewPassword = "NewPass@123",
            ConfirmPassword = "NewPass@123"
        }));
    }

    [Fact]
    public async Task ResetPasswordAsync_PasswordMismatch_ThrowsBadRequestException()
    {
        var request = new ResetPasswordRequestDto
        {
            Email = "user@example.com",
            ResetToken = "any-token",
            NewPassword = "Password@123",
            ConfirmPassword = "DifferentPassword@123"
        };

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ResetPasswordAsync(request));
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_RefreshTokenExpiresLaterThanAccessToken()
    {
        var user = CreateUser();
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword("Demo@123", user.PasswordHash)).Returns(true);

        await _sut.LoginAsync(new LoginRequestDto { Identifier = "parent1@example.com", Password = "Demo@123" });

        var accessTokenExpiry = _jwtGeneratorMock.Object.GetExpirationDate();
        Assert.NotNull(user.RefreshTokenExpiresAt);
        // Refresh token phải dùng hạn riêng (ngày), không bị gắn vào hạn của access token (phút).
        Assert.True(user.RefreshTokenExpiresAt > accessTokenExpiry.AddDays(1));
        _jwtGeneratorMock.Verify(j => j.GetRefreshTokenExpirationDate(), Times.Once);
    }

    [Fact]
    public async Task ForgotPasswordAsync_SuspendedAccount_DoesNotIssueTokenOrSendEmail()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Suspended;
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await _sut.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = "parent1@example.com" });

        Assert.Null(user.ResetTokenHash);
        Assert.Null(user.ResetTokenExpiresAt);
        Assert.Equal(AccountStatus.Suspended, user.Status);
        _emailSenderMock.Verify(e => e.SendPasswordResetEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForgotPasswordAsync_CalledTwice_SecondTokenInvalidatesFirst()
    {
        var user = CreateUser();
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _jwtGeneratorMock.SetupSequence(j => j.GenerateRefreshToken())
            .Returns("first-raw-token")
            .Returns("second-raw-token");

        await _sut.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = "parent1@example.com" });
        var firstHash = user.ResetTokenHash;
        await _sut.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = "parent1@example.com" });

        Assert.Equal(TokenHasher.Hash("first-raw-token"), firstHash);
        Assert.Equal(TokenHasher.Hash("second-raw-token"), user.ResetTokenHash);
        Assert.NotEqual(firstHash, user.ResetTokenHash);
    }

    [Fact]
    public async Task LogoutAsync_SuspendedAccount_KeepsSuspendedStatusButClearsRefreshToken()
    {
        var user = CreateUser();
        user.RefreshTokenHash = "some-hash";
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(1);
        user.Status = AccountStatus.Suspended;

        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await _sut.LogoutAsync(1);

        Assert.Equal(AccountStatus.Suspended, user.Status);
        Assert.Null(user.RefreshTokenHash);
        Assert.Null(user.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task ResetPasswordAsync_SuspendedAccount_KeepsSuspendedStatusButResetsPassword()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Suspended;
        user.ResetTokenHash = TokenHasher.Hash("valid-raw-reset-token");
        user.ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(30);
        user.RefreshTokenHash = "some-old-refresh-hash";
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(1);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.HashPassword("NewPass@123")).Returns("new-hashed-password");

        await _sut.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = user.Email,
            ResetToken = "valid-raw-reset-token",
            NewPassword = "NewPass@123",
            ConfirmPassword = "NewPass@123"
        });

        Assert.Equal(AccountStatus.Suspended, user.Status);
        Assert.Equal("new-hashed-password", user.PasswordHash);
        Assert.Null(user.ResetTokenHash);
        Assert.Null(user.RefreshTokenHash);
        Assert.Null(user.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task RefreshTokenAsync_SuspendedAccount_ThrowsUnauthorizedException()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Suspended;
        user.RefreshTokenHash = TokenHasher.Hash("still-valid-raw-token");
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(1);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _sut.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "still-valid-raw-token" }));

        // Không được cấp cặp token mới cho tài khoản bị khoá.
        Assert.Equal(TokenHasher.Hash("still-valid-raw-token"), user.RefreshTokenHash);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangePasswordAsync_ValidCredentials_UpdatesHashAndRevokesRefreshToken()
    {
        var user = CreateUser();
        user.PasswordHash = "old-hashed-password";
        user.RefreshTokenHash = "active-refresh-token";
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(7);

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword("CurrentPass@123", "old-hashed-password")).Returns(true);
        _passwordHasherMock.Setup(p => p.HashPassword("NewPass@123")).Returns("new-hashed-password");

        await _sut.ChangePasswordAsync(user.Id, new ChangePasswordRequestDto
        {
            CurrentPassword = "CurrentPass@123",
            NewPassword = "NewPass@123",
            ConfirmPassword = "NewPass@123"
        });

        Assert.Equal("new-hashed-password", user.PasswordHash);
        Assert.Null(user.RefreshTokenHash);
        Assert.Null(user.RefreshTokenExpiresAt);
        Assert.NotNull(user.UpdatedAt);
        _userRepoMock.Verify(r => r.Update(user), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangePasswordAsync_PasswordMismatch_ThrowsBadRequestException()
    {
        var request = new ChangePasswordRequestDto
        {
            CurrentPassword = "CurrentPass@123",
            NewPassword = "NewPass@123",
            ConfirmPassword = "MismatchNewPass@123"
        };

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ChangePasswordAsync(1, request));
    }

    [Fact]
    public async Task ChangePasswordAsync_SameOldAndNewPassword_ThrowsBadRequestException()
    {
        var request = new ChangePasswordRequestDto
        {
            CurrentPassword = "SamePassword@123",
            NewPassword = "SamePassword@123",
            ConfirmPassword = "SamePassword@123"
        };

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ChangePasswordAsync(1, request));
    }

    [Fact]
    public async Task ChangePasswordAsync_IncorrectCurrentPassword_ThrowsBadRequestException()
    {
        var user = CreateUser();
        user.PasswordHash = "current-hash";

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword("WrongCurrentPass@123", "current-hash")).Returns(false);

        var request = new ChangePasswordRequestDto
        {
            CurrentPassword = "WrongCurrentPass@123",
            NewPassword = "NewPass@123",
            ConfirmPassword = "NewPass@123"
        };

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ChangePasswordAsync(user.Id, request));
    }

    [Fact]
    public async Task ChangePasswordAsync_SuspendedAccount_ThrowsForbiddenException()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Suspended;

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var request = new ChangePasswordRequestDto
        {
            CurrentPassword = "CurrentPass@123",
            NewPassword = "NewPass@123",
            ConfirmPassword = "NewPass@123"
        };

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ChangePasswordAsync(user.Id, request));
    }

    [Fact]
    public async Task ChangePasswordAsync_UserNotFound_ThrowsNotFoundException()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((UserAccount?)null);

        var request = new ChangePasswordRequestDto
        {
            CurrentPassword = "CurrentPass@123",
            NewPassword = "NewPass@123",
            ConfirmPassword = "NewPass@123"
        };

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ChangePasswordAsync(999, request));
    }

    [Fact]
    public async Task VerifyEmailAsync_ValidToken_SetsStatusToEmailVerifiedAndClearsToken()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Registered;
        user.EmailVerificationTokenHash = TokenHasher.Hash("valid-raw-verification-token");
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddHours(12);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await _sut.VerifyEmailAsync(new VerifyEmailRequestDto { Email = user.Email, Token = "valid-raw-verification-token" });

        Assert.Equal(AccountStatus.EmailVerified, user.Status);
        Assert.Null(user.EmailVerificationTokenHash);
        Assert.Null(user.EmailVerificationTokenExpiresAt);
    }

    [Fact]
    public async Task VerifyEmailAsync_InvalidToken_ThrowsBadRequestException()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Registered;
        user.EmailVerificationTokenHash = TokenHasher.Hash("correct-token");
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddHours(12);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.VerifyEmailAsync(new VerifyEmailRequestDto { Email = user.Email, Token = "wrong-token" }));
    }

    [Fact]
    public async Task VerifyEmailAsync_ExpiredToken_ThrowsBadRequestException()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Registered;
        user.EmailVerificationTokenHash = TokenHasher.Hash("expired-token");
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddHours(-1);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.VerifyEmailAsync(new VerifyEmailRequestDto { Email = user.Email, Token = "expired-token" }));
    }

    [Fact]
    public async Task LoginAsync_UnverifiedAccount_ThrowsForbiddenException()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Registered;
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.LoginAsync(new LoginRequestDto { Identifier = "parent1@example.com", Password = "Demo@123" }));
    }
}
