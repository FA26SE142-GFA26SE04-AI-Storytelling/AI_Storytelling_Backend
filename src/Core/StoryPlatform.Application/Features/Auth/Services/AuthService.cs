using System;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Security;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Application.Features.Auth.DTOs;
using StoryPlatform.Application.Features.Auth.Interfaces;

namespace StoryPlatform.Application.Features.Auth.Services;

public class AuthService : IAuthService
{
    private const string MfaIssuer = "AI Storytelling Platform";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IEmailSender _emailSender;
    private readonly ITotpService _totpService;

    public AuthService(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IEmailSender emailSender,
        ITotpService totpService)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _emailSender = emailSender;
        _totpService = totpService;
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        const int MaxFailedLoginAttempts = 5;
        const int LockoutMinutes = 15;

        var userRepo = _unitOfWork.Repository<UserAccount>();

        // Cho phép đăng nhập bằng cả Email hoặc Username
        var normalizedIdentifier = request.Identifier.Trim().ToLowerInvariant();
        var user = await userRepo.FirstOrDefaultAsync(
            u => u.Email.ToLower() == normalizedIdentifier || u.Username.ToLower() == normalizedIdentifier,
            cancellationToken: cancellationToken);

        if (user == null)
        {
            throw new BadRequestException("Tên đăng nhập hoặc mật khẩu không chính xác.");
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
        {
            throw new ForbiddenException($"Tài khoản tạm thời bị khoá do đăng nhập sai quá {MaxFailedLoginAttempts} lần. Vui lòng thử lại sau {LockoutMinutes} phút.");
        }

        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            user.FailedLoginAttempts += 1;
            if (user.FailedLoginAttempts >= MaxFailedLoginAttempts)
            {
                user.LockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
                user.FailedLoginAttempts = 0;
            }
            userRepo.Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            throw new BadRequestException("Tên đăng nhập hoặc mật khẩu không chính xác.");
        }

        if (user.Status == AccountStatus.Suspended)
        {
            throw new ForbiddenException("Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên.");
        }

        if (user.Status == AccountStatus.Registered)
        {
            throw new ForbiddenException("Vui lòng xác thực email trước khi đăng nhập. Kiểm tra hộp thư của bạn để lấy mã xác thực.");
        }

        if (user.Role == UserRole.Administrator)
        {
            return await BuildMfaChallengeResponseAsync(user, cancellationToken);
        }

        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.Status = AccountStatus.LoggedIn;
        user.LastLoginAt = DateTime.UtcNow;

        return await GenerateAuthResponseAsync(user, cancellationToken);
    }

    public async Task<AuthResponseDto> VerifyMfaAsync(VerifyMfaRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!_jwtTokenGenerator.TryValidateMfaChallengeToken(request.ChallengeToken, out var userId))
        {
            throw new UnauthorizedException("Challenge token không hợp lệ hoặc đã hết hạn. Vui lòng đăng nhập lại.");
        }

        var userRepo = _unitOfWork.Repository<UserAccount>();
        var user = await userRepo.GetByIdAsync(userId, cancellationToken)
                   ?? throw new UnauthorizedException("Challenge token không hợp lệ hoặc đã hết hạn. Vui lòng đăng nhập lại.");

        if (string.IsNullOrEmpty(user.MfaSecret) || !_totpService.VerifyCode(user.MfaSecret, request.Code))
        {
            throw new BadRequestException("Mã xác thực không đúng.");
        }

        user.MfaEnabled = true;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.Status = AccountStatus.LoggedIn;
        user.LastLoginAt = DateTime.UtcNow;

        return await GenerateAuthResponseAsync(user, cancellationToken);
    }

    private async Task<AuthResponseDto> BuildMfaChallengeResponseAsync(UserAccount user, CancellationToken cancellationToken)
    {
        if (user.MfaEnabled)
        {
            return new AuthResponseDto
            {
                MfaRequired = true,
                MfaChallengeToken = _jwtTokenGenerator.GenerateMfaChallengeToken(user.Id)
            };
        }

        if (string.IsNullOrEmpty(user.MfaSecret))
        {
            user.MfaSecret = _totpService.GenerateSecret();
            _unitOfWork.Repository<UserAccount>().Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new AuthResponseDto
        {
            MfaSetupRequired = true,
            MfaChallengeToken = _jwtTokenGenerator.GenerateMfaChallengeToken(user.Id),
            MfaProvisioningUri = _totpService.BuildProvisioningUri(user.MfaSecret, user.Email, MfaIssuer)
        };
    }

    public async Task<AuthResponseDto> RefreshTokenAsync(RefreshTokenRequestDto request, CancellationToken cancellationToken = default)
    {
        var hashedToken = TokenHasher.Hash(request.RefreshToken);
        var refreshTokenRepo = _unitOfWork.Repository<RefreshToken>();

        var tokenRow = await refreshTokenRepo.FirstOrDefaultAsync(
            rt => rt.TokenHash == hashedToken && rt.RevokedAt == null,
            "UserAccount",
            cancellationToken);

        if (tokenRow == null || tokenRow.UserAccount == null || tokenRow.RevokedAt != null
            || tokenRow.ExpiresAt <= DateTime.UtcNow || tokenRow.UserAccount.IsDeleted
            || tokenRow.SessionScope == SessionScope.Child)
        {
            throw new UnauthorizedException("Refresh token không hợp lệ hoặc đã hết hạn. Vui lòng đăng nhập lại.");
        }

        var user = tokenRow.UserAccount;

        // Tài khoản bị khoá sau khi đã đăng nhập không được phép làm mới phiên.
        if (user.Status == AccountStatus.Suspended || user.Status == AccountStatus.Registered)
        {
            throw new UnauthorizedException("Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên.");
        }

        // Xoay vòng: thu hồi dòng refresh token cũ trước khi cấp dòng mới.
        tokenRow.RevokedAt = DateTime.UtcNow;
        refreshTokenRepo.Update(tokenRow);

        return await GenerateAuthResponseAsync(user, cancellationToken);
    }

    public async Task RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request.Role != UserRole.Parent && request.Role != UserRole.Teacher)
        {
            throw new BadRequestException("Chỉ có thể tự đăng ký tài khoản với vai trò Parent hoặc Teacher.");
        }

        if (request.Password != request.ConfirmPassword)
        {
            throw new BadRequestException("Mật khẩu xác nhận không khớp.");
        }

        var userRepo = _unitOfWork.Repository<UserAccount>();

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var emailExists = await userRepo.ExistsAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);
        if (emailExists)
        {
            throw new BadRequestException($"Email '{request.Email}' đã được sử dụng trong hệ thống.");
        }

        var normalizedUsername = request.Username.Trim().ToLowerInvariant();
        var usernameExists = await userRepo.ExistsAsync(u => u.Username.ToLower() == normalizedUsername, cancellationToken);
        if (usernameExists)
        {
            throw new BadRequestException($"Tên đăng nhập '{request.Username}' đã tồn tại.");
        }

        var rawVerificationToken = _jwtTokenGenerator.GenerateRefreshToken(); // tái dùng bộ sinh chuỗi ngẫu nhiên an toàn sẵn có

        var newUser = new UserAccount
        {
            Username = request.Username.Trim(),
            Email = normalizedEmail,
            FullName = request.FullName.Trim(),
            PhoneNumber = request.PhoneNumber,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            Role = request.Role,
            Status = AccountStatus.Registered,
            EmailVerificationTokenHash = TokenHasher.Hash(rawVerificationToken),
            EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedAt = DateTime.UtcNow
        };

        await userRepo.AddAsync(newUser, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _emailSender.SendEmailVerificationEmailAsync(newUser.Email, newUser.FullName, rawVerificationToken, cancellationToken);
    }

    public async Task<UserProfileDto> GetCurrentUserProfileAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new NotFoundException("Tài khoản", userId);
        }

        return MapToUserProfileDto(user);
    }

    public async Task LogoutAsync(int userId, LogoutRequestDto request, CancellationToken cancellationToken = default)
    {
        var userRepo = _unitOfWork.Repository<UserAccount>();
        var user = await userRepo.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new NotFoundException("Tài khoản", userId);
        }

        var hashedToken = TokenHasher.Hash(request.RefreshToken);
        var refreshTokenRepo = _unitOfWork.Repository<RefreshToken>();
        var tokenRow = await refreshTokenRepo.FirstOrDefaultAsync(
            rt => rt.TokenHash == hashedToken && rt.UserAccountId == userId,
            cancellationToken: cancellationToken);

        // Đăng xuất là hành động idempotent — nếu token đã bị thu hồi/không xác định,
        // vẫn chuyển trạng thái tài khoản về LoggedOut thay vì báo lỗi.
        if (tokenRow != null && tokenRow.RevokedAt == null)
        {
            tokenRow.RevokedAt = DateTime.UtcNow;
            refreshTokenRepo.Update(tokenRow);
        }

        // Không hạ cấp trạng thái Suspended — tài khoản bị khoá phải giữ nguyên trạng thái khoá.
        if (user.Status != AccountStatus.Suspended)
        {
            user.Status = AccountStatus.LoggedOut;
        }
        userRepo.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task LogoutAllDevicesAsync(int userId, CancellationToken cancellationToken = default)
    {
        var userRepo = _unitOfWork.Repository<UserAccount>();
        var user = await userRepo.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new NotFoundException("Tài khoản", userId);
        }

        await RevokeRefreshTokensAsync(userId, cancellationToken);
        user.RefreshTokenHash = null;
        user.RefreshTokenExpiresAt = null;
        await WriteAuthAuditAsync(user.Id, "LOGOUT_ALL_DEVICES", cancellationToken);

        // JWT đã phát hành sẽ bị từ chối ở request tiếp theo khi version không còn khớp.
        user.TokenVersion += 1;
        if (user.Status != AccountStatus.Suspended)
        {
            user.Status = AccountStatus.LoggedOut;
        }
        userRepo.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var userRepo = _unitOfWork.Repository<UserAccount>();
        var user = await userRepo.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken: cancellationToken);

        // Không tiết lộ email có tồn tại hay không — âm thầm bỏ qua nếu không tìm thấy.
        if (user == null)
        {
            return;
        }

        // Tài khoản bị khoá không được phép tự đặt lại mật khẩu để thoát trạng thái Suspended.
        // Im lặng bỏ qua y hệt trường hợp không tìm thấy email.
        if (user.Status == AccountStatus.Suspended)
        {
            return;
        }

        var rawResetToken = _jwtTokenGenerator.GenerateRefreshToken(); // tái dùng bộ sinh chuỗi ngẫu nhiên an toàn sẵn có
        user.ResetTokenHash = TokenHasher.Hash(rawResetToken);
        user.ResetTokenExpiresAt = DateTime.UtcNow.AddHours(1);
        user.Status = AccountStatus.PasswordResetPending;
        userRepo.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _emailSender.SendPasswordResetEmailAsync(user.Email, user.FullName, rawResetToken, cancellationToken);
    }

    public async Task VerifyEmailAsync(VerifyEmailRequestDto request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var userRepo = _unitOfWork.Repository<UserAccount>();
        var user = await userRepo.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken: cancellationToken);

        var hashedIncomingToken = TokenHasher.Hash(request.Token);
        if (user == null || user.EmailVerificationTokenHash != hashedIncomingToken)
        {
            throw new BadRequestException("Mã xác thực email không hợp lệ.");
        }

        if (user.EmailVerificationTokenExpiresAt == null || user.EmailVerificationTokenExpiresAt < DateTime.UtcNow)
        {
            throw new BadRequestException("Mã xác thực email đã hết hạn.");
        }

        user.EmailVerificationTokenHash = null;
        user.EmailVerificationTokenExpiresAt = null;
        // Chỉ nâng cấp trạng thái Registered -> EmailVerified. Không hạ cấp một tài khoản
        // đã LoggedIn/LoggedOut/PasswordResetPending, và không mở khoá Suspended qua đường này.
        if (user.Status == AccountStatus.Registered)
        {
            user.Status = AccountStatus.EmailVerified;
        }
        userRepo.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ResendVerificationEmailAsync(
        ResendVerificationEmailRequestDto request, CancellationToken cancellationToken = default)
    {
        const int CooldownSeconds = 60;

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var userRepo = _unitOfWork.Repository<UserAccount>();
        var user = await userRepo.FirstOrDefaultAsync(
            account => account.Email.ToLower() == normalizedEmail,
            cancellationToken: cancellationToken);

        // Luôn im lặng bỏ qua để không tiết lộ email có tồn tại hoặc đã xác thực hay chưa.
        if (user == null || user.Status != AccountStatus.Registered)
        {
            return;
        }

        var cooldownStart = DateTime.UtcNow.AddSeconds(-CooldownSeconds);
        var sentRecently = await _unitOfWork.Repository<AuditLog>().ExistsAsync(
            log => log.EntityType == nameof(UserAccount)
                   && log.EntityId == user.Id
                   && log.Action == "RESEND_VERIFICATION_EMAIL"
                   && log.OccurredAt > cooldownStart,
            cancellationToken);
        if (sentRecently)
        {
            return;
        }

        var rawVerificationToken = _jwtTokenGenerator.GenerateRefreshToken();
        user.EmailVerificationTokenHash = TokenHasher.Hash(rawVerificationToken);
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddHours(24);
        userRepo.Update(user);
        await WriteAuthAuditAsync(user.Id, "RESEND_VERIFICATION_EMAIL", cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _emailSender.SendEmailVerificationEmailAsync(
            user.Email, user.FullName, rawVerificationToken, cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request.NewPassword != request.ConfirmPassword)
        {
            throw new BadRequestException("Mật khẩu xác nhận không khớp.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var userRepo = _unitOfWork.Repository<UserAccount>();
        var user = await userRepo.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken: cancellationToken);

        var hashedIncomingToken = TokenHasher.Hash(request.ResetToken);
        if (user == null || user.ResetTokenHash != hashedIncomingToken)
        {
            throw new BadRequestException("Token đặt lại mật khẩu không hợp lệ.");
        }

        if (user.ResetTokenExpiresAt == null || user.ResetTokenExpiresAt < DateTime.UtcNow)
        {
            throw new BadRequestException("Token đặt lại mật khẩu đã hết hạn. Vui lòng yêu cầu lại.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.ResetTokenHash = null;
        user.ResetTokenExpiresAt = null;
        // Vô hiệu hoá phiên đăng nhập hiện tại — buộc đăng nhập lại bằng mật khẩu mới ở mọi thiết bị.
        user.RefreshTokenHash = null;
        user.RefreshTokenExpiresAt = null;
        user.TokenVersion += 1;
        await RevokeRefreshTokensAsync(user.Id, cancellationToken);
        await WriteAuthAuditAsync(user.Id, "RESET_PASSWORD", cancellationToken);
        // Không hạ cấp trạng thái Suspended — đặt lại mật khẩu không được dùng để tự mở khoá tài khoản.
        if (user.Status != AccountStatus.Suspended)
        {
            user.Status = AccountStatus.LoggedOut;
        }
        userRepo.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ChangePasswordAsync(int userId, ChangePasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request.NewPassword != request.ConfirmPassword)
        {
            throw new BadRequestException("Mật khẩu xác nhận không khớp.");
        }

        if (request.CurrentPassword == request.NewPassword)
        {
            throw new BadRequestException("Mật khẩu mới không được trùng với mật khẩu hiện tại.");
        }

        var userRepo = _unitOfWork.Repository<UserAccount>();
        var user = await userRepo.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new NotFoundException("Tài khoản", userId);
        }

        if (user.Status == AccountStatus.Suspended)
        {
            throw new ForbiddenException("Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên.");
        }

        if (!_passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
        {
            throw new BadRequestException("Mật khẩu hiện tại không chính xác.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.RefreshTokenHash = null;
        user.RefreshTokenExpiresAt = null;
        user.TokenVersion += 1;
        await RevokeRefreshTokensAsync(user.Id, cancellationToken);
        await WriteAuthAuditAsync(user.Id, "CHANGE_PASSWORD", cancellationToken);
        user.UpdatedAt = DateTime.UtcNow;

        userRepo.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<SessionDto>> ListSessionsAsync(
        int userId, CancellationToken cancellationToken = default)
    {
        var tokens = await _unitOfWork.Repository<RefreshToken>().FindAsync(
            token => token.UserAccountId == userId
                     && token.RevokedAt == null
                     && token.ExpiresAt > DateTime.UtcNow,
            cancellationToken: cancellationToken);

        return tokens
            .OrderByDescending(token => token.IssuedAt)
            .Select(token => new SessionDto
            {
                Id = token.Id,
                IssuedAt = token.IssuedAt,
                ExpiresAt = token.ExpiresAt
            })
            .ToList();
    }

    public async Task RevokeSessionAsync(
        int userId, int sessionId, CancellationToken cancellationToken = default)
    {
        var repository = _unitOfWork.Repository<RefreshToken>();
        var token = await repository.GetByIdAsync(sessionId, cancellationToken);
        if (token == null || token.UserAccountId != userId)
        {
            throw new NotFoundException("Phiên đăng nhập", sessionId);
        }

        if (token.RevokedAt != null)
        {
            return;
        }

        token.RevokedAt = DateTime.UtcNow;
        repository.Update(token);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthResponseDto> GenerateAuthResponseAsync(UserAccount user, CancellationToken cancellationToken)
    {
        var accessToken = _jwtTokenGenerator.GenerateAccessToken(user);
        var rawRefreshToken = _jwtTokenGenerator.GenerateRefreshToken();
        var refreshTokenExpiresAt = _jwtTokenGenerator.GetRefreshTokenExpirationDate();

        await _unitOfWork.Repository<RefreshToken>().AddAsync(new RefreshToken
        {
            UserAccountId = user.Id,
            TokenHash = TokenHasher.Hash(rawRefreshToken),
            SessionScope = user.Role == UserRole.Administrator ? SessionScope.Admin : SessionScope.Supervisor,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = refreshTokenExpiresAt
        }, cancellationToken);

        // Login và refresh chỉ cấp phiên cho tài khoản đã tồn tại.
        _unitOfWork.Repository<UserAccount>().Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken,
            TokenType = "Bearer",
            ExpiresInSeconds = _jwtTokenGenerator.ExpiresInSeconds,
            User = MapToUserProfileDto(user)
        };
    }

    private async Task RevokeRefreshTokensAsync(int userId, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.Repository<RefreshToken>();
        var tokens = await repository.FindAsync(
            token => token.UserAccountId == userId && token.RevokedAt == null,
            cancellationToken: cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var token in tokens)
        {
            token.RevokedAt = now;
            repository.Update(token);
        }
    }

    private async Task WriteAuthAuditAsync(int userId, string action, CancellationToken cancellationToken)
    {
        await _unitOfWork.Repository<AuditLog>().AddAsync(new AuditLog
        {
            ActorUserId = userId,
            Action = action,
            EntityType = nameof(UserAccount),
            EntityId = userId,
            OccurredAt = DateTime.UtcNow
        }, cancellationToken);
    }

    private static UserProfileDto MapToUserProfileDto(UserAccount user)
    {
        return new UserProfileDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            AvatarUrl = user.AvatarUrl,
            Role = user.Role.ToString(),
            Status = user.Status.ToString()
        };
    }
}
