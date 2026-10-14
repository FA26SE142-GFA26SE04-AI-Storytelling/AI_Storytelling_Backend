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
using StoryPlatform.Application.Features.Auth.Interfaces;
using StoryPlatform.Application.Features.Auth.Services;
using StoryPlatform.Application.Features.ChildProfiles.Sessions.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Auth;

public class AuthServiceTests
{
    private readonly Mock<IGenericRepository<RefreshToken>> _refreshTokenRepoMock = new();
    private readonly Mock<IGenericRepository<AuditLog>> _auditLogRepoMock = new();
    private readonly Mock<IGenericRepository<UserAccount>> _userRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IJwtTokenGenerator> _jwtGeneratorMock = new();
    private readonly Mock<IEmailSender> _emailSenderMock = new();
    private readonly Mock<ITotpService> _totpServiceMock = new();
    private readonly Mock<IChildSessionRevoker> _childSessionRevokerMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly AuthService _sut;

    [Fact]
    public void AccountStatus_MatchesSpecValuesOnly()
    {
        Assert.Equal(
            new[] { "Registered", "Active", "PasswordResetPending", "Suspended" },
            Enum.GetNames<AccountStatus>());
    }

    public AuthServiceTests()
    {
        _unitOfWorkMock.Setup(u => u.Repository<UserAccount>()).Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Repository<RefreshToken>()).Returns(_refreshTokenRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Repository<AuditLog>()).Returns(_auditLogRepoMock.Object);
        _refreshTokenRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<RefreshToken, bool>>>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<RefreshToken>());
        _jwtGeneratorMock.Setup(j => j.GenerateAccessToken(It.IsAny<UserAccount>())).Returns("fake-access-token");
        _jwtGeneratorMock.Setup(j => j.GenerateRefreshToken()).Returns("fake-refresh-token-raw");
        _jwtGeneratorMock.Setup(j => j.GetExpirationDate()).Returns(DateTime.UtcNow.AddMinutes(120));
        _jwtGeneratorMock.Setup(j => j.GetRefreshTokenExpirationDate()).Returns(DateTime.UtcNow.AddDays(7));
        _jwtGeneratorMock.Setup(j => j.ExpiresInSeconds).Returns(7200L);

        _sut = new AuthService(
            _unitOfWorkMock.Object, _passwordHasherMock.Object, _jwtGeneratorMock.Object,
            _emailSenderMock.Object, _totpServiceMock.Object, _childSessionRevokerMock.Object,
            _notificationServiceMock.Object);
    }

    private static UserAccount CreateUser(string password = "hashed-password") => new()
    {
        Id = 1,
        Username = "parent_demo1",
        Email = "parent1@example.com",
        PasswordHash = password,
        FullName = "Tran Thi Hoa",
        Role = UserRole.Parent,
        Status = AccountStatus.Active
    };

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsTokensAndSavesUser()
    {
        var user = CreateUser();
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword("Demo@123", user.PasswordHash)).Returns(true);

        var result = await _sut.LoginAsync(new LoginRequestDto { Identifier = "parent1@example.com", Password = "Demo@123" });

        Assert.Equal("fake-access-token", result.AccessToken);
        Assert.Equal("fake-refresh-token-raw", result.RefreshToken);

        _userRepoMock.Verify(r => r.Update(user), Times.AtLeastOnce);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_Success_DoesNotChangeAccountStatus()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Active;
        _passwordHasherMock.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await _sut.LoginAsync(new LoginRequestDto { Identifier = user.Email, Password = "Password1" });

        Assert.Equal(AccountStatus.Active, user.Status);
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
    public async Task LoginAsync_PasswordResetPendingAccount_ThrowsForbiddenException()
    {
        var user = CreateUser();
        user.Status = AccountStatus.PasswordResetPending;
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword(
                It.IsAny<string>(), It.IsAny<string>()))
            .Returns(true);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.LoginAsync(new LoginRequestDto
            {
                Identifier = user.Email,
                Password = "discarded-initial-password"
            }));

        _refreshTokenRepoMock.Verify(
            r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
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
        string? sentOtp = null;
        _emailSenderMock.Setup(e => e.SendEmailVerificationEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>(
                (_, _, otp, _) => sentOtp = otp)
            .Returns(Task.CompletedTask);

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
        Assert.Matches(@"^\d{6}$", sentOtp!);
        Assert.Equal(
            OtpHelper.Hash(OtpHelper.EmailVerifyPurpose, addedUser.Email, sentOtp!),
            addedUser.EmailVerificationTokenHash);
        Assert.True(addedUser.EmailVerificationTokenExpiresAt > DateTime.UtcNow.AddMinutes(9));
        Assert.True(addedUser.EmailVerificationTokenExpiresAt <= DateTime.UtcNow.AddMinutes(10));
        // Đăng ký không còn cấp phiên đăng nhập ngay — phải xác thực email trước.
        Assert.Null(addedUser.RefreshTokenHash);
        _emailSenderMock.Verify(e => e.SendEmailVerificationEmailAsync(
            "new@example.com", "Nguoi Dung Moi", sentOtp!, It.IsAny<CancellationToken>()), Times.Once);
        // Tài khoản mới (Id == 0) đang ở trạng thái Added của EF — không được gọi Update().
        _userRepoMock.Verify(r => r.Update(It.IsAny<UserAccount>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_AlwaysCreatesParentRoleAccount()
    {
        _userRepoMock.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasherMock.Setup(p => p.HashPassword(It.IsAny<string>())).Returns("hashed-password");
        UserAccount? addedUser = null;
        _userRepoMock
            .Setup(r => r.AddAsync(It.IsAny<UserAccount>(), It.IsAny<CancellationToken>()))
            .Callback<UserAccount, CancellationToken>((u, _) => addedUser = u)
            .ReturnsAsync((UserAccount u, CancellationToken _) => u);

        await _sut.RegisterAsync(new RegisterRequestDto
        {
            Username = "parent_new2",
            Email = "new2@example.com",
            FullName = "Nguoi Dung Moi Hai",
            Password = "Demo@123",
            ConfirmPassword = "Demo@123"
        });

        Assert.Equal(UserRole.Parent, addedUser!.Role);
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

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task RegisterAsync_InvalidEmail_DoesNotPersistOrSendVerificationEmail(string email)
    {
        var request = new RegisterRequestDto
        {
            Username = "parent_invalid_email",
            Email = email,
            FullName = "Invalid Email User",
            Password = "Password@123",
            ConfirmPassword = "Password@123"
        };

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.RegisterAsync(request));

        _userRepoMock.Verify(r => r.ExistsAsync(
            It.IsAny<Expression<Func<UserAccount, bool>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _userRepoMock.Verify(r => r.AddAsync(
            It.IsAny<UserAccount>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Never);
        _emailSenderMock.Verify(e => e.SendEmailVerificationEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogoutAsync_UnknownUserId_ThrowsNotFoundException()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((UserAccount?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.LogoutAsync(999, new LogoutRequestDto { RefreshToken = "any" }));
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
        Assert.NotEqual(AccountStatus.PasswordResetPending, user.Status);
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
        Assert.Equal(AccountStatus.Active, user.Status);
        Assert.Equal(2, user.TokenVersion);
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

        // Refresh token phải dùng hạn riêng (ngày), không bị gắn vào hạn của access token (phút).
        _refreshTokenRepoMock.Verify(r => r.AddAsync(It.Is<RefreshToken>(rt => rt.ExpiresAt > accessTokenExpiry.AddDays(1)), It.IsAny<CancellationToken>()), Times.Once);
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
        var token = new RefreshToken
        {
            UserAccountId = user.Id, UserAccount = user,
            TokenHash = TokenHasher.Hash("still-valid-raw-token"),
            ExpiresAt = DateTime.UtcNow.AddDays(1), SessionScope = SessionScope.Supervisor
        };
        _refreshTokenRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<RefreshToken, bool>>>(), "UserAccount", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _sut.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "still-valid-raw-token" }));

        // Không được cấp cặp token mới cho tài khoản bị khoá.
        Assert.Null(token.RevokedAt);
        _refreshTokenRepoMock.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshTokenAsync_PasswordResetPendingAccount_ThrowsUnauthorizedException()
    {
        var user = CreateUser();
        user.Status = AccountStatus.PasswordResetPending;
        var token = new RefreshToken
        {
            UserAccountId = user.Id,
            UserAccount = user,
            TokenHash = TokenHasher.Hash("still-valid-raw-token"),
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            SessionScope = SessionScope.Supervisor
        };
        _refreshTokenRepoMock
            .Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<RefreshToken, bool>>>(), "UserAccount",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _sut.RefreshTokenAsync(new RefreshTokenRequestDto
            {
                RefreshToken = "still-valid-raw-token"
            }));

        Assert.Null(token.RevokedAt);
        _refreshTokenRepoMock.Verify(
            r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(
            u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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
        Assert.Equal(2, user.TokenVersion);
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
    public async Task VerifyEmailAsync_ValidOtp_VerifiesEmailAndClearsOtp()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Registered;
        user.EmailVerificationTokenHash = OtpHelper.Hash(
            OtpHelper.EmailVerifyPurpose, user.Email, "123456");
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddMinutes(5);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await _sut.VerifyEmailAsync(new VerifyEmailRequestDto { Email = user.Email, Otp = "123456" });

        Assert.Equal(AccountStatus.Active, user.Status);
        Assert.Null(user.EmailVerificationTokenHash);
        Assert.Null(user.EmailVerificationTokenExpiresAt);
        Assert.NotNull(user.EmailVerifiedAt);
        Assert.Equal(0, user.EmailVerificationFailedAttempts);
    }

    [Fact]
    public async Task VerifyEmailAsync_WrongOtp_CountsAttemptAndThrows()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Registered;
        user.EmailVerificationTokenHash = OtpHelper.Hash(
            OtpHelper.EmailVerifyPurpose, user.Email, "123456");
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddMinutes(5);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.VerifyEmailAsync(new VerifyEmailRequestDto { Email = user.Email, Otp = "000000" }));

        Assert.Equal(1, user.EmailVerificationFailedAttempts);
        Assert.Null(user.EmailVerifiedAt);
        _unitOfWorkMock.Verify(
            u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyEmailAsync_ExpiredOtp_ThrowsBadRequestException()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Registered;
        user.EmailVerificationTokenHash = OtpHelper.Hash(
            OtpHelper.EmailVerifyPurpose, user.Email, "123456");
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddMinutes(-1);

        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.VerifyEmailAsync(new VerifyEmailRequestDto { Email = user.Email, Otp = "123456" }));
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

    [Fact]
    public async Task LoginAsync_FifthConsecutiveWrongPassword_LocksAccountFor15Minutes()
    {
        var user = CreateUser();
        user.FailedLoginAttempts = 4;
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.LoginAsync(new LoginRequestDto { Identifier = "parent1@example.com", Password = "WrongPass" }));

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.NotNull(user.LockedUntil);
        Assert.True(user.LockedUntil > DateTime.UtcNow.AddMinutes(14));
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WrongPasswordBelowThreshold_IncrementsCounterWithoutLocking()
    {
        var user = CreateUser();
        user.FailedLoginAttempts = 1;
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.LoginAsync(new LoginRequestDto { Identifier = "parent1@example.com", Password = "WrongPass" }));

        Assert.Equal(2, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public async Task LoginAsync_AccountCurrentlyLocked_ThrowsForbiddenExceptionWithoutCheckingPassword()
    {
        var user = CreateUser();
        user.LockedUntil = DateTime.UtcNow.AddMinutes(10);
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.LoginAsync(new LoginRequestDto { Identifier = "parent1@example.com", Password = "Demo@123" }));

        _passwordHasherMock.Verify(p => p.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_CorrectPasswordAfterPriorFailures_ResetsCounter()
    {
        var user = CreateUser();
        user.FailedLoginAttempts = 3;
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword("Demo@123", user.PasswordHash)).Returns(true);

        await _sut.LoginAsync(new LoginRequestDto { Identifier = "parent1@example.com", Password = "Demo@123" });

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_InsertsNewRefreshTokenRowWithSupervisorScope()
    {
        var user = CreateUser();
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword("Demo@123", user.PasswordHash)).Returns(true);
        RefreshToken? inserted = null;
        _refreshTokenRepoMock
            .Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((rt, _) => inserted = rt)
            .ReturnsAsync((RefreshToken rt, CancellationToken _) => rt);

        var result = await _sut.LoginAsync(new LoginRequestDto { Identifier = "parent1@example.com", Password = "Demo@123" });

        Assert.NotNull(inserted);
        Assert.Equal(user.Id, inserted!.UserAccountId);
        Assert.Equal(TokenHasher.Hash("fake-refresh-token-raw"), inserted.TokenHash);
        Assert.Equal(SessionScope.Supervisor, inserted.SessionScope);
        Assert.Null(inserted.RevokedAt);
        Assert.Equal("fake-refresh-token-raw", result.RefreshToken);
        // Cột đơn cũ trên UserAccount không còn được dùng cho phiên mới.
        Assert.Null(user.RefreshTokenHash);
    }

    [Fact]
    public async Task RefreshTokenAsync_ValidToken_RevokesOldRowAndInsertsNewRow()
    {
        var user = CreateUser();
        var oldToken = new RefreshToken
        {
            Id = 10,
            UserAccountId = user.Id,
            UserAccount = user,
            TokenHash = TokenHasher.Hash("old-raw-refresh-token"),
            SessionScope = SessionScope.Supervisor,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            RevokedAt = null
        };
        _refreshTokenRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<RefreshToken, bool>>>(), "UserAccount", It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldToken);
        RefreshToken? inserted = null;
        _refreshTokenRepoMock
            .Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((rt, _) => inserted = rt)
            .ReturnsAsync((RefreshToken rt, CancellationToken _) => rt);

        var result = await _sut.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "old-raw-refresh-token" });

        Assert.NotNull(oldToken.RevokedAt);
        Assert.NotNull(inserted);
        Assert.Equal(TokenHasher.Hash("fake-refresh-token-raw"), inserted!.TokenHash);
        Assert.Equal("fake-access-token", result.AccessToken);
        _refreshTokenRepoMock.Verify(r => r.Update(oldToken), Times.Once);
    }

    [Fact]
    public async Task RefreshTokenAsync_UnknownOrRevokedToken_ThrowsUnauthorizedException()
    {
        _refreshTokenRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<RefreshToken, bool>>>(), "UserAccount", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _sut.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "unknown-token" }));
    }

    [Fact]
    public async Task RefreshTokenAsync_ExpiredToken_ThrowsUnauthorizedException()
    {
        var user = CreateUser();
        var expiredToken = new RefreshToken
        {
            Id = 11,
            UserAccountId = user.Id,
            UserAccount = user,
            TokenHash = TokenHasher.Hash("expired-raw-token"),
            SessionScope = SessionScope.Supervisor,
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            RevokedAt = null
        };
        _refreshTokenRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<RefreshToken, bool>>>(), "UserAccount", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredToken);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _sut.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "expired-raw-token" }));
    }

    [Fact]
    public async Task LogoutAsync_RevokesOnlyMatchingRefreshTokenRowAndKeepsAccountStatus()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Active;
        var currentDeviceToken = new RefreshToken
        {
            Id = 20,
            UserAccountId = user.Id,
            TokenHash = TokenHasher.Hash("current-device-raw-token"),
            SessionScope = SessionScope.Supervisor,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };
        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _refreshTokenRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<RefreshToken, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentDeviceToken);

        await _sut.LogoutAsync(1, new LogoutRequestDto { RefreshToken = "current-device-raw-token" });

        Assert.NotNull(currentDeviceToken.RevokedAt);
        Assert.Equal(AccountStatus.Active, user.Status);
        _refreshTokenRepoMock.Verify(r => r.Update(currentDeviceToken), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_UnknownRefreshToken_StillKeepsAccountStatusIdempotently()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Active;
        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _refreshTokenRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<RefreshToken, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        await _sut.LogoutAsync(1, new LogoutRequestDto { RefreshToken = "already-revoked-or-unknown" });

        Assert.Equal(AccountStatus.Active, user.Status);
        _refreshTokenRepoMock.Verify(r => r.Update(It.IsAny<RefreshToken>()), Times.Never);
    }

    [Fact]
    public async Task LogoutAllDevicesAsync_RevokesEveryActiveRefreshTokenRowForUser()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Active;
        user.TokenVersion = 1;
        var rows = new List<RefreshToken>
        {
            new() { Id = 30, UserAccountId = user.Id, RevokedAt = null },
            new() { Id = 31, UserAccountId = user.Id, RevokedAt = null }
        };
        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _refreshTokenRepoMock
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<RefreshToken, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        await _sut.LogoutAllDevicesAsync(1);

        Assert.All(rows, r => Assert.NotNull(r.RevokedAt));
        Assert.Equal(2, user.TokenVersion);
        Assert.Equal(AccountStatus.Active, user.Status);
        _refreshTokenRepoMock.Verify(r => r.Update(It.IsAny<RefreshToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task LogoutAllDevicesAsync_RevokesSupervisorChildSessionsOfEveryChild()
    {
        var user = CreateUser();
        _userRepoMock.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _refreshTokenRepoMock
            .Setup(repository => repository.FindAsync(
                It.IsAny<Expression<Func<RefreshToken, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RefreshToken>());

        await _sut.LogoutAllDevicesAsync(1);

        _childSessionRevokerMock.Verify(
            revoker => revoker.RevokeBySupervisorAsync(1, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogoutAllDevicesAsync_UnknownUserId_ThrowsNotFoundException()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((UserAccount?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.LogoutAllDevicesAsync(999));
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidToken_WritesAuditLogEntry()
    {
        var user = CreateUser();
        user.ResetTokenHash = TokenHasher.Hash("valid-raw-reset-token");
        user.ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(30);
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.HashPassword(It.IsAny<string>())).Returns("new-hashed-password");
        AuditLog? logged = null;
        _auditLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => logged = log)
            .ReturnsAsync((AuditLog log, CancellationToken _) => log);

        await _sut.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = user.Email,
            ResetToken = "valid-raw-reset-token",
            NewPassword = "NewPass@123",
            ConfirmPassword = "NewPass@123"
        });

        Assert.NotNull(logged);
        Assert.Equal(user.Id, logged!.ActorUserId);
        Assert.Equal("RESET_PASSWORD", logged.Action);
        Assert.Equal("UserAccount", logged.EntityType);
        Assert.Equal(user.Id, logged.EntityId);
    }

    [Fact]
    public async Task ChangePasswordAsync_ValidCredentials_WritesAuditLogEntry()
    {
        var user = CreateUser();
        user.PasswordHash = "old-hashed-password";
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword("CurrentPass@123", "old-hashed-password")).Returns(true);
        _passwordHasherMock.Setup(p => p.HashPassword("NewPass@123")).Returns("new-hashed-password");
        AuditLog? logged = null;
        _auditLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => logged = log)
            .ReturnsAsync((AuditLog log, CancellationToken _) => log);

        await _sut.ChangePasswordAsync(user.Id, new ChangePasswordRequestDto
        {
            CurrentPassword = "CurrentPass@123",
            NewPassword = "NewPass@123",
            ConfirmPassword = "NewPass@123"
        });

        Assert.NotNull(logged);
        Assert.Equal("CHANGE_PASSWORD", logged!.Action);
        Assert.Equal("UserAccount", logged.EntityType);
        Assert.Equal(user.Id, logged.EntityId);
    }

    [Fact]
    public async Task LogoutAllDevicesAsync_WritesAuditLogEntry()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Active;
        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _refreshTokenRepoMock
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<RefreshToken, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RefreshToken>());
        AuditLog? logged = null;
        _auditLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => logged = log)
            .ReturnsAsync((AuditLog log, CancellationToken _) => log);

        await _sut.LogoutAllDevicesAsync(1);

        Assert.NotNull(logged);
        Assert.Equal("LOGOUT_ALL_DEVICES", logged!.Action);
    }

    [Fact]
    public async Task RegisterAsync_NoRoleSpecified_DefaultsToParent()
    {
        _userRepoMock.Setup(r => r.ExistsAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _passwordHasherMock.Setup(p => p.HashPassword(It.IsAny<string>())).Returns("hashed-password");
        UserAccount? addedUser = null;
        _userRepoMock
            .Setup(r => r.AddAsync(It.IsAny<UserAccount>(), It.IsAny<CancellationToken>()))
            .Callback<UserAccount, CancellationToken>((u, _) => addedUser = u)
            .ReturnsAsync((UserAccount u, CancellationToken _) => u);

        await _sut.RegisterAsync(new RegisterRequestDto
        {
            Username = "parent_default_role",
            Email = "parent.default@example.com",
            FullName = "Phu Huynh Mac Dinh",
            Password = "Demo@123",
            ConfirmPassword = "Demo@123"
            // Role không set — phải mặc định Parent (đúng hành vi hiện tại đã có test RegisterAsync_NewEmail_CreatesUserAndSendsVerificationEmail bên trên).
        });

        Assert.Equal(UserRole.Parent, addedUser!.Role);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PasswordUpdate_RevokesOnlyUsersActiveSessions(bool resetPassword)
    {
        var user = CreateUser();
        user.ResetTokenHash = TokenHasher.Hash("reset-token");
        user.ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(30);
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword("CurrentPass@123", user.PasswordHash)).Returns(true);
        var alreadyRevoked = DateTime.UtcNow.AddDays(-1);
        var rows = new List<RefreshToken>
        {
            new() { Id = 1, UserAccountId = user.Id },
            new() { Id = 2, UserAccountId = user.Id },
            new() { Id = 3, UserAccountId = 99 },
            new() { Id = 4, UserAccountId = user.Id, RevokedAt = alreadyRevoked }
        };
        _refreshTokenRepoMock
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<RefreshToken, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<RefreshToken, bool>> predicate, string? _, CancellationToken _) =>
                (IReadOnlyList<RefreshToken>)rows.Where(predicate.Compile()).ToList());

        if (resetPassword)
            await _sut.ResetPasswordAsync(new ResetPasswordRequestDto { Email = user.Email, ResetToken = "reset-token", NewPassword = "NewPass@123", ConfirmPassword = "NewPass@123" });
        else
            await _sut.ChangePasswordAsync(user.Id, new ChangePasswordRequestDto { CurrentPassword = "CurrentPass@123", NewPassword = "NewPass@123", ConfirmPassword = "NewPass@123" });

        Assert.NotNull(rows[0].RevokedAt);
        Assert.NotNull(rows[1].RevokedAt);
        Assert.Null(rows[2].RevokedAt);
        Assert.Equal(alreadyRevoked, rows[3].RevokedAt);
        Assert.Equal(2, user.TokenVersion);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("current", AccountStatus.Active, true)]
    [InlineData("other-device", AccountStatus.Active, false)]
    [InlineData("foreign", AccountStatus.Active, false)]
    [InlineData("current", AccountStatus.Suspended, true)]
    public async Task LogoutAsync_EnforcesTokenOwnershipAndPreservesOtherSessions(string rawToken, AccountStatus status, bool revokeCurrent)
    {
        var user = CreateUser();
        user.Status = status;
        var rows = new List<RefreshToken>
        {
            new() { Id = 1, UserAccountId = user.Id, TokenHash = TokenHasher.Hash("current") },
            new() { Id = 2, UserAccountId = user.Id, TokenHash = TokenHasher.Hash("other-device") },
            new() { Id = 3, UserAccountId = 99, TokenHash = TokenHasher.Hash("foreign") }
        };
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _refreshTokenRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<RefreshToken, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<RefreshToken, bool>> predicate, string? _, CancellationToken _) => rows.FirstOrDefault(predicate.Compile()));

        await _sut.LogoutAsync(user.Id, new LogoutRequestDto { RefreshToken = rawToken });

        Assert.Equal(revokeCurrent, rows[0].RevokedAt.HasValue);
        Assert.Equal(rawToken == "other-device", rows[1].RevokedAt.HasValue);
        Assert.Null(rows[2].RevokedAt);
        Assert.Equal(1, user.TokenVersion);
        Assert.Equal(status, user.Status);
    }

    [Fact]
    public async Task LoginAsync_ExpiredLock_AllowsLoginAndClearsLock()
    {
        var user = CreateUser();
        user.LockedUntil = DateTime.UtcNow.AddMinutes(-1);
        _userRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword("correct", user.PasswordHash)).Returns(true);

        await _sut.LoginAsync(new LoginRequestDto { Identifier = user.Email, Password = "correct" });

        Assert.Null(user.LockedUntil);
        Assert.Equal(0, user.FailedLoginAttempts);
    }

    [Theory]
    [InlineData(true, SessionScope.Supervisor)]
    [InlineData(false, SessionScope.Child)]
    public async Task RefreshTokenAsync_RevokedOrChildToken_CannotIssueSupervisorTokens(bool revoked, SessionScope scope)
    {
        var token = new RefreshToken
        {
            UserAccount = CreateUser(), UserAccountId = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(1), SessionScope = scope,
            RevokedAt = revoked ? DateTime.UtcNow.AddMinutes(-1) : null
        };
        _refreshTokenRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<RefreshToken, bool>>>(), "UserAccount", It.IsAny<CancellationToken>())).ReturnsAsync(token);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "token" }));

        _refreshTokenRepoMock.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_RegisteredUnverifiedUser_SendsNewTokenAndWritesAuditLog()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Registered;
        _userRepoMock.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _auditLogRepoMock.Setup(repo => repo.ExistsAsync(
                It.IsAny<Expression<Func<AuditLog, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await _sut.ResendVerificationEmailAsync(new ResendVerificationEmailRequestDto { Email = user.Email });

        _emailSenderMock.Verify(sender => sender.SendEmailVerificationEmailAsync(
            user.Email, user.FullName, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _auditLogRepoMock.Verify(repo => repo.AddAsync(
            It.Is<AuditLog>(log => log.Action == "RESEND_VERIFICATION_EMAIL" && log.EntityId == user.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_AlreadyVerifiedUser_SilentlyDoesNothing()
    {
        var user = CreateUser();
        _userRepoMock.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await _sut.ResendVerificationEmailAsync(new ResendVerificationEmailRequestDto { Email = user.Email });

        _emailSenderMock.Verify(sender => sender.SendEmailVerificationEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_WithinCooldown_SilentlyDoesNothing()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Registered;
        _userRepoMock.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _auditLogRepoMock.Setup(repo => repo.ExistsAsync(
                It.IsAny<Expression<Func<AuditLog, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _sut.ResendVerificationEmailAsync(new ResendVerificationEmailRequestDto { Email = user.Email });

        _emailSenderMock.Verify(sender => sender.SendEmailVerificationEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListSessionsAsync_ReturnsOnlyActiveNonExpiredTokensOfCurrentUser()
    {
        var now = DateTime.UtcNow;
        var tokens = new[]
        {
            new RefreshToken { Id = 1, UserAccountId = 1, IssuedAt = now.AddDays(-1), ExpiresAt = now.AddDays(6) },
            new RefreshToken { Id = 2, UserAccountId = 1, IssuedAt = now.AddDays(-2), ExpiresAt = now.AddDays(-1) },
            new RefreshToken
            {
                Id = 3, UserAccountId = 1, IssuedAt = now.AddDays(-3),
                ExpiresAt = now.AddDays(4), RevokedAt = now
            },
            new RefreshToken { Id = 4, UserAccountId = 2, IssuedAt = now, ExpiresAt = now.AddDays(6) },
            new RefreshToken { Id = 5, UserAccountId = 1, IssuedAt = now, ExpiresAt = now.AddDays(6) }
        };
        _refreshTokenRepoMock.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<RefreshToken, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<RefreshToken, bool>> predicate, string? _, CancellationToken _) =>
                tokens.Where(predicate.Compile()).ToList());

        var result = await _sut.ListSessionsAsync(1);

        Assert.Equal(new[] { 5, 1 }, result.Select(session => session.Id));
    }

    [Fact]
    public async Task RevokeSessionAsync_TokenBelongsToAnotherUser_ThrowsNotFoundException()
    {
        _refreshTokenRepoMock.Setup(repo => repo.GetByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshToken { Id = 10, UserAccountId = 999 });

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.RevokeSessionAsync(1, 10));
    }

    [Fact]
    public async Task RevokeSessionAsync_ValidOwnedToken_SetsRevokedAtAndSaves()
    {
        var token = new RefreshToken { Id = 10, UserAccountId = 1 };
        _refreshTokenRepoMock.Setup(repo => repo.GetByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        await _sut.RevokeSessionAsync(1, 10);

        Assert.NotNull(token.RevokedAt);
        _refreshTokenRepoMock.Verify(repo => repo.Update(token), Times.Once);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeSessionAsync_AlreadyRevokedToken_IsIdempotent()
    {
        var token = new RefreshToken
        {
            Id = 10,
            UserAccountId = 1,
            RevokedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        _refreshTokenRepoMock.Setup(repo => repo.GetByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        await _sut.RevokeSessionAsync(1, 10);

        _refreshTokenRepoMock.Verify(repo => repo.Update(It.IsAny<RefreshToken>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- Administrator MFA (Muc 9) ----------

    private static UserAccount CreateAdminUser(bool mfaEnabled, string? mfaSecret = null) => new()
    {
        Id = 7,
        Username = "admin_demo",
        Email = "admin@example.com",
        PasswordHash = "hashed-password",
        FullName = "Admin Demo",
        Role = UserRole.Administrator,
        Status = AccountStatus.Active,
        MfaEnabled = mfaEnabled,
        MfaSecret = mfaSecret
    };

    [Fact]
    public async Task LoginAsync_AdministratorWithMfaEnabled_ReturnsMfaRequiredWithoutTokens()
    {
        var admin = CreateAdminUser(mfaEnabled: true, mfaSecret: "JBSWY3DPEHPK3PXP");
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(admin);
        _passwordHasherMock.Setup(p => p.VerifyPassword("Demo@123", admin.PasswordHash)).Returns(true);
        _jwtGeneratorMock.Setup(j => j.GenerateMfaChallengeToken(admin.Id)).Returns("challenge-token");

        var result = await _sut.LoginAsync(new LoginRequestDto { Identifier = admin.Email, Password = "Demo@123" });

        Assert.True(result.MfaRequired);
        Assert.False(result.MfaSetupRequired);
        Assert.Equal("challenge-token", result.MfaChallengeToken);
        Assert.Empty(result.AccessToken);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_AdministratorWithoutMfaSetup_GeneratesSecretAndReturnsSetupRequired()
    {
        var admin = CreateAdminUser(mfaEnabled: false, mfaSecret: null);
        _userRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(admin);
        _passwordHasherMock.Setup(p => p.VerifyPassword("Demo@123", admin.PasswordHash)).Returns(true);
        _totpServiceMock.Setup(t => t.GenerateSecret()).Returns("NEWSECRET234567");
        _totpServiceMock.Setup(t => t.BuildProvisioningUri("NEWSECRET234567", admin.Email, It.IsAny<string>()))
            .Returns("otpauth://totp/fake");
        _jwtGeneratorMock.Setup(j => j.GenerateMfaChallengeToken(admin.Id)).Returns("setup-challenge-token");

        var result = await _sut.LoginAsync(new LoginRequestDto { Identifier = admin.Email, Password = "Demo@123" });

        Assert.True(result.MfaSetupRequired);
        Assert.Equal("otpauth://totp/fake", result.MfaProvisioningUri);
        Assert.Equal("setup-challenge-token", result.MfaChallengeToken);
        Assert.Equal("NEWSECRET234567", admin.MfaSecret);
        Assert.False(admin.MfaEnabled);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyMfaAsync_InvalidChallengeToken_ThrowsUnauthorized()
    {
        _jwtGeneratorMock.Setup(j => j.TryValidateMfaChallengeToken("bad", out It.Ref<int>.IsAny)).Returns(false);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.VerifyMfaAsync(
            new VerifyMfaRequestDto { ChallengeToken = "bad", Code = "123456" }));
    }

    private delegate bool TryValidateCallback(string token, out int userId);

    [Fact]
    public async Task VerifyMfaAsync_WrongCode_ThrowsBadRequest()
    {
        var admin = CreateAdminUser(mfaEnabled: true, mfaSecret: "JBSWY3DPEHPK3PXP");
        _jwtGeneratorMock
            .Setup(j => j.TryValidateMfaChallengeToken("token", out It.Ref<int>.IsAny))
            .Returns(new TryValidateCallback((string _, out int userId) =>
            {
                userId = admin.Id;
                return true;
            }));
        _userRepoMock.Setup(r => r.GetByIdAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        _totpServiceMock.Setup(t => t.VerifyCode(admin.MfaSecret!, "000000")).Returns(false);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.VerifyMfaAsync(
            new VerifyMfaRequestDto { ChallengeToken = "token", Code = "000000" }));
    }

    [Fact]
    public async Task VerifyMfaAsync_ValidCodeFirstTimeSetup_EnablesMfaAndReturnsTokens()
    {
        var admin = CreateAdminUser(mfaEnabled: false, mfaSecret: "NEWSECRET234567");
        _jwtGeneratorMock
            .Setup(j => j.TryValidateMfaChallengeToken("token", out It.Ref<int>.IsAny))
            .Returns(new TryValidateCallback((string _, out int userId) =>
            {
                userId = admin.Id;
                return true;
            }));
        _userRepoMock.Setup(r => r.GetByIdAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        _totpServiceMock.Setup(t => t.VerifyCode(admin.MfaSecret!, "654321")).Returns(true);

        var result = await _sut.VerifyMfaAsync(new VerifyMfaRequestDto { ChallengeToken = "token", Code = "654321" });

        Assert.True(admin.MfaEnabled);
        Assert.Equal("fake-access-token", result.AccessToken);
        Assert.False(result.MfaRequired);
        Assert.False(result.MfaSetupRequired);
    }

    [Fact]
    public async Task VerifyEmailAsync_TooManyWrongAttempts_RejectsEvenCorrectOtp()
    {
        var user = CreateUser();
        user.Status = AccountStatus.Registered;
        user.EmailVerificationTokenHash = OtpHelper.Hash(
            OtpHelper.EmailVerifyPurpose, user.Email, "123456");
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddMinutes(5);
        user.EmailVerificationFailedAttempts = OtpHelper.MaxAttempts;
        _userRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.VerifyEmailAsync(new VerifyEmailRequestDto
            {
                Email = user.Email,
                Otp = "123456"
            }));

        Assert.Null(user.EmailVerifiedAt);
    }

    [Fact]
    public async Task ChangePasswordAsync_WithCurrentRefreshToken_KeepsCurrentSessionRevokesOthersAndNotifies()
    {
        var user = CreateUser();
        user.TokenVersion = 3;
        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword("CurrentPass@123", user.PasswordHash))
            .Returns(true);
        _passwordHasherMock.Setup(p => p.HashPassword("NewPass@123"))
            .Returns("new-hash");
        var current = new RefreshToken { Id = 30, UserAccountId = 1 };
        var other = new RefreshToken { Id = 31, UserAccountId = 1 };
        _refreshTokenRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<RefreshToken, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);
        _refreshTokenRepoMock.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<RefreshToken, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RefreshToken> { current, other });

        await _sut.ChangePasswordAsync(1, new ChangePasswordRequestDto
        {
            CurrentPassword = "CurrentPass@123",
            NewPassword = "NewPass@123",
            ConfirmPassword = "NewPass@123",
            CurrentRefreshToken = "current-raw-token"
        });

        Assert.Equal("new-hash", user.PasswordHash);
        Assert.NotNull(user.PasswordChangedAt);
        Assert.Null(current.RevokedAt);
        Assert.NotNull(other.RevokedAt);
        Assert.Equal(3, user.TokenVersion);
        _childSessionRevokerMock.Verify(r => r.RevokeBySupervisorExceptSessionAsync(
            1, 30, It.IsAny<CancellationToken>()), Times.Once);
        _notificationServiceMock.Verify(n => n.CreateAsync(
            1, NotificationType.AccountSecurityChanged, It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _emailSenderMock.Verify(e => e.SendAccountSecurityChangedEmailAsync(
            user.Email, user.FullName, AccountSecurityChange.PasswordChanged,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongCurrentPasswordFifthTime_LocksAccountLikeLogin()
    {
        var user = CreateUser();
        user.FailedLoginAttempts = 4;
        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword("wrong", user.PasswordHash))
            .Returns(false);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ChangePasswordAsync(1, new ChangePasswordRequestDto
            {
                CurrentPassword = "wrong",
                NewPassword = "NewPass@123",
                ConfirmPassword = "NewPass@123"
            }));

        Assert.True(user.LockedUntil > DateTime.UtcNow.AddMinutes(14));
        Assert.Equal(0, user.FailedLoginAttempts);
    }

    [Fact]
    public async Task RequestEmailChangeAsync_NonParent_ThrowsForbidden()
    {
        var user = CreateUser();
        user.Role = UserRole.Administrator;
        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.RequestEmailChangeAsync(1, new RequestEmailChangeRequestDto
            {
                CurrentPassword = "x",
                NewEmail = "new@example.com"
            }));
    }

    [Fact]
    public async Task RequestEmailChangeAsync_Valid_StoresPendingEmailAndSendsOtpToNewEmail()
    {
        var user = CreateUser();
        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(p => p.VerifyPassword("CurrentPass@123", user.PasswordHash))
            .Returns(true);
        _userRepoMock.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        string? sentOtp = null;
        _emailSenderMock.Setup(e => e.SendEmailChangeOtpEmailAsync(
                "new@example.com", user.FullName, It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>(
                (_, _, otp, _) => sentOtp = otp)
            .Returns(Task.CompletedTask);

        await _sut.RequestEmailChangeAsync(1, new RequestEmailChangeRequestDto
        {
            CurrentPassword = "CurrentPass@123",
            NewEmail = "  New@Example.com "
        });

        Assert.Equal("new@example.com", user.PendingEmail);
        Assert.Equal(
            OtpHelper.Hash(OtpHelper.EmailChangePurpose, "new@example.com", sentOtp!),
            user.PendingEmailOtpHash);
        Assert.True(user.PendingEmailOtpExpiresAt > DateTime.UtcNow.AddMinutes(9));
        Assert.Equal("parent1@example.com", user.Email);
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_ValidOtp_SwitchesEmailAndNotifiesOldAddress()
    {
        var user = CreateUser();
        user.PendingEmail = "new@example.com";
        user.PendingEmailOtpHash = OtpHelper.Hash(
            OtpHelper.EmailChangePurpose, "new@example.com", "123456");
        user.PendingEmailOtpExpiresAt = DateTime.UtcNow.AddMinutes(5);
        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepoMock.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await _sut.ConfirmEmailChangeAsync(
            1, new ConfirmEmailChangeRequestDto { Otp = "123456" });

        Assert.Equal("new@example.com", user.Email);
        Assert.NotNull(user.EmailVerifiedAt);
        Assert.Null(user.PendingEmail);
        Assert.Null(user.PendingEmailOtpHash);
        _emailSenderMock.Verify(e => e.SendAccountSecurityChangedEmailAsync(
            "parent1@example.com", user.FullName, AccountSecurityChange.EmailChanged,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_WrongOtp_CountsAttemptAndKeepsOldEmail()
    {
        var user = CreateUser();
        user.PendingEmail = "new@example.com";
        user.PendingEmailOtpHash = OtpHelper.Hash(
            OtpHelper.EmailChangePurpose, "new@example.com", "123456");
        user.PendingEmailOtpExpiresAt = DateTime.UtcNow.AddMinutes(5);
        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ConfirmEmailChangeAsync(
                1, new ConfirmEmailChangeRequestDto { Otp = "000000" }));

        Assert.Equal(1, user.PendingEmailOtpFailedAttempts);
        Assert.Equal("parent1@example.com", user.Email);
    }

    [Fact]
    public async Task UpdateProfileAsync_Valid_UpdatesNameAndAvatarAndWritesAudit()
    {
        var user = CreateUser();
        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _sut.UpdateProfileAsync(1, new UpdateProfileRequestDto
        {
            FullName = "  Nguyen Van A ",
            AvatarUrl = "https://cdn.example.com/a.png"
        });

        Assert.Equal("Nguyen Van A", user.FullName);
        Assert.Equal("https://cdn.example.com/a.png", user.AvatarUrl);
        Assert.Equal("Nguyen Van A", result.FullName);
        _auditLogRepoMock.Verify(r => r.AddAsync(
            It.Is<AuditLog>(log => log.Action == "UPDATE_PROFILE" && log.EntityId == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateProfileAsync_NonHttpAvatar_ThrowsBadRequest()
    {
        var user = CreateUser();
        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.UpdateProfileAsync(1, new UpdateProfileRequestDto
            {
                FullName = "An",
                AvatarUrl = "javascript:alert(1)"
            }));
    }
}
