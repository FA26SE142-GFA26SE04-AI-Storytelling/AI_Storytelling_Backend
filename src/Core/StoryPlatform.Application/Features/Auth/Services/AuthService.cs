using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
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
using StoryPlatform.Application.Features.ChildProfiles.Sessions.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;

namespace StoryPlatform.Application.Features.Auth.Services;

public class AuthService : IAuthService
{
    private const int MaxFailedLoginAttempts = 5;
    private const int LockoutMinutes = 15;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IEmailSender _emailSender;
    private readonly IChildSessionRevoker _childSessionRevoker;
    private readonly INotificationService _notificationService;

    public AuthService(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IEmailSender emailSender,
        IChildSessionRevoker childSessionRevoker,
        INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _emailSender = emailSender;
        _childSessionRevoker = childSessionRevoker;
        _notificationService = notificationService;
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
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
            ApplyFailedLogin(user);
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

        if (user.Status == AccountStatus.PasswordResetPending)
        {
            throw new ForbiddenException(
                "Vui lòng thiết lập mật khẩu bằng mã đã được gửi qua email trước khi đăng nhập.");
        }

        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTime.UtcNow;

        return await GenerateAuthResponseAsync(user, cancellationToken);
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
        if (user.Status is AccountStatus.Suspended or AccountStatus.Registered)
        {
            throw new UnauthorizedException("Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên.");
        }

        if (user.Status == AccountStatus.PasswordResetPending)
        {
            throw new UnauthorizedException(
                "Tài khoản đang chờ thiết lập mật khẩu và không thể làm mới phiên đăng nhập.");
        }

        // Xoay vòng: thu hồi dòng refresh token cũ trước khi cấp dòng mới.
        tokenRow.RevokedAt = DateTime.UtcNow;
        refreshTokenRepo.Update(tokenRow);

        return await GenerateAuthResponseAsync(user, cancellationToken);
    }

    public async Task RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request.Password != request.ConfirmPassword)
        {
            throw new BadRequestException("Mật khẩu xác nhận không khớp.");
        }

        var normalizedEmail = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedEmail)
            || !new EmailAddressAttribute().IsValid(normalizedEmail))
        {
            throw new BadRequestException("Email không đúng định dạng.");
        }

        var userRepo = _unitOfWork.Repository<UserAccount>();
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

        var otp = OtpHelper.Generate();

        var newUser = new UserAccount
        {
            Username = request.Username.Trim(),
            Email = normalizedEmail,
            FullName = request.FullName.Trim(),
            PhoneNumber = request.PhoneNumber,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            // Public self-registration only creates Parent accounts. Operational accounts
            // must be provisioned by an Administrator.
            Role = UserRole.User,
            Status = AccountStatus.Registered,
            EmailVerificationTokenHash = OtpHelper.Hash(OtpHelper.EmailVerifyPurpose, normalizedEmail, otp),
            EmailVerificationTokenExpiresAt = DateTime.UtcNow.Add(OtpHelper.Ttl),
            CreatedAt = DateTime.UtcNow
        };

        await userRepo.AddAsync(newUser, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _emailSender.SendEmailVerificationEmailAsync(
            newUser.Email, newUser.FullName, otp, cancellationToken);
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

    public async Task<UserProfileDto> UpdateProfileAsync(
        int userId, UpdateProfileRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var userRepo = _unitOfWork.Repository<UserAccount>();
        var user = await userRepo.GetByIdAsync(userId, cancellationToken)
                   ?? throw new NotFoundException("Tài khoản", userId);

        var fullName = request.FullName?.Trim() ?? string.Empty;
        if (fullName.Length == 0)
        {
            throw new BadRequestException("Họ và tên không được để trống.");
        }

        var avatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl)
            ? null
            : request.AvatarUrl.Trim();
        if (avatarUrl != null
            && !(Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri)
                 && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)))
        {
            throw new BadRequestException(
                "Địa chỉ ảnh đại diện phải là đường dẫn http hoặc https.");
        }

        user.FullName = fullName;
        user.AvatarUrl = avatarUrl;
        user.UpdatedAt = DateTime.UtcNow;
        userRepo.Update(user);
        await WriteAuthAuditAsync(user.Id, "UPDATE_PROFILE", user.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

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

        // Đăng xuất là hành động idempotent — token đã thu hồi/không xác định vẫn thành công.
        if (tokenRow != null && tokenRow.RevokedAt == null)
        {
            tokenRow.RevokedAt = DateTime.UtcNow;
            refreshTokenRepo.Update(tokenRow);
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
        // Mục 7c: thu hồi Child Session vào qua ProfileSwitcher / "Cho bé đọc truyện này" của người này.
        // Phiên Easy Login không có SupervisorSession nên không bị ảnh hưởng.
        await _childSessionRevoker.RevokeBySupervisorAsync(userId, null, cancellationToken);
        user.RefreshTokenHash = null;
        user.RefreshTokenExpiresAt = null;
        await WriteAuthAuditAsync(user.Id, "LOGOUT_ALL_DEVICES", user.Id, cancellationToken);

        // JWT đã phát hành sẽ bị từ chối ở request tiếp theo khi version không còn khớp.
        user.TokenVersion += 1;
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

        if (IsUnactivatedModerator(user))
        {
            return;
        }

        var rawResetToken = _jwtTokenGenerator.GenerateRefreshToken(); // tái dùng bộ sinh chuỗi ngẫu nhiên an toàn sẵn có
        user.ResetTokenHash = TokenHasher.Hash(rawResetToken);
        user.ResetTokenExpiresAt = DateTime.UtcNow.AddHours(1);
        // UC-L1B-02: quên mật khẩu không đổi trạng thái tài khoản — nếu không, người lạ chỉ cần
        // nhập email nạn nhân vào "Quên mật khẩu" là khóa được đăng nhập của họ.
        userRepo.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _emailSender.SendPasswordResetEmailAsync(user.Email, user.FullName, rawResetToken, cancellationToken);
    }

    public async Task VerifyEmailAsync(VerifyEmailRequestDto request, CancellationToken cancellationToken = default)
    {
        const string invalidMessage = "Mã xác thực email không hợp lệ.";
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var userRepo = _unitOfWork.Repository<UserAccount>();
        var user = await userRepo.FirstOrDefaultAsync(
            u => u.Email.ToLower() == normalizedEmail,
            cancellationToken: cancellationToken);

        if (user == null || user.EmailVerificationTokenHash == null)
        {
            throw new BadRequestException(invalidMessage);
        }

        if (user.EmailVerificationFailedAttempts >= OtpHelper.MaxAttempts)
        {
            throw new BadRequestException(
                "Bạn đã nhập sai quá nhiều lần. Vui lòng yêu cầu gửi lại mã xác thực.");
        }

        if (user.EmailVerificationTokenExpiresAt == null || user.EmailVerificationTokenExpiresAt < DateTime.UtcNow)
        {
            throw new BadRequestException(
                "Mã xác thực email đã hết hạn. Vui lòng yêu cầu gửi lại mã.");
        }

        if (!OtpHelper.Matches(
                user.EmailVerificationTokenHash, OtpHelper.EmailVerifyPurpose,
                normalizedEmail, request.Otp))
        {
            user.EmailVerificationFailedAttempts += 1;
            userRepo.Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new BadRequestException(invalidMessage);
        }

        user.EmailVerificationTokenHash = null;
        user.EmailVerificationTokenExpiresAt = null;
        user.EmailVerificationFailedAttempts = 0;
        user.EmailVerifiedAt = DateTime.UtcNow;
        // Chỉ nâng cấp trạng thái Registered -> Active; không mở khoá Suspended qua đường này.
        if (user.Status == AccountStatus.Registered)
        {
            user.Status = AccountStatus.Active;
        }
        userRepo.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ResendVerificationEmailAsync(
        ResendVerificationEmailRequestDto request, CancellationToken cancellationToken = default)
    {
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

        var cooldownStart = DateTime.UtcNow - OtpHelper.ResendCooldown;
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

        var otp = OtpHelper.Generate();
        user.EmailVerificationTokenHash = OtpHelper.Hash(
            OtpHelper.EmailVerifyPurpose, user.Email, otp);
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.Add(OtpHelper.Ttl);
        user.EmailVerificationFailedAttempts = 0;
        userRepo.Update(user);
        await WriteAuthAuditAsync(user.Id, "RESEND_VERIFICATION_EMAIL", user.Id, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _emailSender.SendEmailVerificationEmailAsync(
            user.Email, user.FullName, otp, cancellationToken);
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

        if (IsUnactivatedModerator(user))
        {
            throw new BadRequestException(
                "Tài khoản Moderator cần được kích hoạt qua trang kích hoạt tài khoản bằng mã mời.");
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
        await WriteAuthAuditAsync(user.Id, "RESET_PASSWORD", user.Id, cancellationToken);
        // Không hạ cấp trạng thái Suspended — đặt lại mật khẩu không được dùng để tự mở khoá tài khoản.
        if (user.Status != AccountStatus.Suspended)
        {
            user.Status = AccountStatus.Active;
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

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
        {
            throw new ForbiddenException(
                $"Tài khoản tạm thời bị khoá do nhập sai mật khẩu quá {MaxFailedLoginAttempts} lần. Vui lòng thử lại sau {LockoutMinutes} phút.");
        }

        if (!_passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
        {
            ApplyFailedLogin(user);
            userRepo.Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new BadRequestException("Mật khẩu hiện tại không chính xác.");
        }

        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.PasswordChangedAt = DateTime.UtcNow;
        user.RefreshTokenHash = null;
        user.RefreshTokenExpiresAt = null;

        RefreshToken? currentSession = null;
        if (!string.IsNullOrWhiteSpace(request.CurrentRefreshToken))
        {
            var hash = TokenHasher.Hash(request.CurrentRefreshToken.Trim());
            currentSession = await _unitOfWork.Repository<RefreshToken>().FirstOrDefaultAsync(
                token => token.TokenHash == hash
                         && token.UserAccountId == userId
                         && token.RevokedAt == null,
                cancellationToken: cancellationToken);
        }

        if (currentSession != null)
        {
            await RevokeRefreshTokensExceptAsync(userId, currentSession.Id, cancellationToken);
            await _childSessionRevoker.RevokeBySupervisorExceptSessionAsync(
                userId, currentSession.Id, cancellationToken);
        }
        else
        {
            user.TokenVersion += 1;
            await RevokeRefreshTokensAsync(user.Id, cancellationToken);
            await _childSessionRevoker.RevokeBySupervisorAsync(userId, null, cancellationToken);
        }

        await WriteAuthAuditAsync(user.Id, "CHANGE_PASSWORD", user.Id, cancellationToken);
        user.UpdatedAt = DateTime.UtcNow;

        userRepo.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await NotifySecurityChangeAsync(
            user, AccountSecurityChange.PasswordChanged, null, cancellationToken);
    }

    public async Task RequestEmailChangeAsync(
        int userId, RequestEmailChangeRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var userRepo = _unitOfWork.Repository<UserAccount>();
        var user = await userRepo.GetByIdAsync(userId, cancellationToken)
                   ?? throw new NotFoundException("Tài khoản", userId);

        if (user.Role != UserRole.User)
        {
            throw new ForbiddenException(
                "Tài khoản vận hành không tự đổi email — vui lòng liên hệ Administrator.");
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
        {
            throw new ForbiddenException(
                $"Tài khoản tạm thời bị khoá do nhập sai mật khẩu quá {MaxFailedLoginAttempts} lần. Vui lòng thử lại sau {LockoutMinutes} phút.");
        }

        if (!_passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
        {
            ApplyFailedLogin(user);
            userRepo.Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new BadRequestException("Mật khẩu hiện tại không chính xác.");
        }

        var newEmail = request.NewEmail.Trim().ToLowerInvariant();
        if (!new EmailAddressAttribute().IsValid(newEmail))
        {
            throw new BadRequestException("Email không đúng định dạng.");
        }

        if (newEmail == user.Email)
        {
            throw new BadRequestException("Email mới phải khác email hiện tại.");
        }

        if (await userRepo.ExistsAsync(
                value => value.Email.ToLower() == newEmail && value.Id != userId,
                cancellationToken))
        {
            throw new BadRequestException(
                "Email này đã được sử dụng bởi tài khoản khác.");
        }

        var now = DateTime.UtcNow;
        if (user.PendingEmailOtpExpiresAt.HasValue
            && user.PendingEmailOtpExpiresAt.Value - OtpHelper.Ttl
            + OtpHelper.ResendCooldown > now)
        {
            throw new BadRequestException(
                "Vui lòng đợi một chút trước khi yêu cầu gửi lại mã.");
        }

        var otp = OtpHelper.Generate();
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.PendingEmail = newEmail;
        user.PendingEmailOtpHash = OtpHelper.Hash(
            OtpHelper.EmailChangePurpose, newEmail, otp);
        user.PendingEmailOtpExpiresAt = now.Add(OtpHelper.Ttl);
        user.PendingEmailOtpFailedAttempts = 0;
        userRepo.Update(user);
        await WriteAuthAuditAsync(
            user.Id, "REQUEST_EMAIL_CHANGE", user.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _emailSender.SendEmailChangeOtpEmailAsync(
            newEmail, user.FullName, otp, cancellationToken);
    }

    public async Task ConfirmEmailChangeAsync(
        int userId, ConfirmEmailChangeRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var userRepo = _unitOfWork.Repository<UserAccount>();
        var user = await userRepo.GetByIdAsync(userId, cancellationToken)
                   ?? throw new NotFoundException("Tài khoản", userId);

        if (user.Role != UserRole.User)
        {
            throw new ForbiddenException(
                "Tài khoản vận hành không tự đổi email — vui lòng liên hệ Administrator.");
        }

        if (user.PendingEmail == null || user.PendingEmailOtpHash == null)
        {
            throw new BadRequestException(
                "Không có yêu cầu đổi email nào đang chờ xác thực.");
        }

        if (user.PendingEmailOtpFailedAttempts >= OtpHelper.MaxAttempts)
        {
            throw new BadRequestException(
                "Bạn đã nhập sai quá nhiều lần. Vui lòng gửi lại yêu cầu đổi email.");
        }

        if (user.PendingEmailOtpExpiresAt == null
            || user.PendingEmailOtpExpiresAt < DateTime.UtcNow)
        {
            throw new BadRequestException(
                "Mã xác thực đã hết hạn. Vui lòng gửi lại yêu cầu đổi email.");
        }

        if (!OtpHelper.Matches(
                user.PendingEmailOtpHash, OtpHelper.EmailChangePurpose,
                user.PendingEmail, request.Otp))
        {
            user.PendingEmailOtpFailedAttempts += 1;
            userRepo.Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new BadRequestException("Mã xác thực không hợp lệ.");
        }

        var newEmail = user.PendingEmail;
        if (await userRepo.ExistsAsync(
                value => value.Email.ToLower() == newEmail && value.Id != userId,
                cancellationToken))
        {
            throw new BadRequestException(
                "Email này đã được sử dụng bởi tài khoản khác.");
        }

        var oldEmail = user.Email;
        user.Email = newEmail;
        user.EmailVerifiedAt = DateTime.UtcNow;
        user.PendingEmail = null;
        user.PendingEmailOtpHash = null;
        user.PendingEmailOtpExpiresAt = null;
        user.PendingEmailOtpFailedAttempts = 0;
        user.UpdatedAt = DateTime.UtcNow;
        userRepo.Update(user);
        await WriteAuthAuditAsync(user.Id, "CHANGE_EMAIL", user.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await NotifySecurityChangeAsync(
            user, AccountSecurityChange.EmailChanged, oldEmail, cancellationToken);
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
            SessionScope = user.Role switch
            {
                UserRole.Administrator => SessionScope.Admin,
                UserRole.Moderator => SessionScope.Moderator,
                _ => SessionScope.Supervisor
            },
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

    private async Task RevokeRefreshTokensExceptAsync(
        int userId, int keepTokenId, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.Repository<RefreshToken>();
        var tokens = await repository.FindAsync(
            token => token.UserAccountId == userId
                     && token.RevokedAt == null
                     && token.Id != keepTokenId,
            cancellationToken: cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var token in tokens.Where(token => token.Id != keepTokenId))
        {
            token.RevokedAt = now;
            repository.Update(token);
        }
    }

    private static void ApplyFailedLogin(UserAccount user)
    {
        user.FailedLoginAttempts += 1;
        if (user.FailedLoginAttempts >= MaxFailedLoginAttempts)
        {
            user.LockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
            user.FailedLoginAttempts = 0;
        }
    }

    /// <summary>
    /// Moderator được mời nhưng chưa kích hoạt phải đi qua trang kích hoạt để xác nhận cam kết,
    /// không được dùng luồng đặt lại mật khẩu với mã mời lưu chung trong ResetToken.
    /// </summary>
    private static bool IsUnactivatedModerator(UserAccount user) =>
        user.Role == UserRole.Moderator && user.CommitmentAcceptedAt == null;

    private async Task NotifySecurityChangeAsync(
        UserAccount user, AccountSecurityChange change, string? emailTo,
        CancellationToken cancellationToken)
    {
        await _notificationService.CreateAsync(
            user.Id, NotificationType.AccountSecurityChanged,
            JsonSerializer.Serialize(new { change = change.ToString() }), cancellationToken);
        await _emailSender.SendAccountSecurityChangedEmailAsync(
            emailTo ?? user.Email, user.FullName, change, cancellationToken);
    }

    private async Task WriteAuthAuditAsync(
        int actorUserId, string action, int entityId, CancellationToken cancellationToken)
    {
        await _unitOfWork.Repository<AuditLog>().AddAsync(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = nameof(UserAccount),
            EntityId = entityId,
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
