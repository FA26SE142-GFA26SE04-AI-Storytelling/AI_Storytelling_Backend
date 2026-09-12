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
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IEmailSender _emailSender;

    public AuthService(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IEmailSender emailSender)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _emailSender = emailSender;
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var userRepo = _unitOfWork.Repository<UserAccount>();

        // Cho phép đăng nhập bằng cả Email hoặc Username
        var normalizedIdentifier = request.Identifier.Trim().ToLowerInvariant();
        var user = await userRepo.FirstOrDefaultAsync(
            u => u.Email.ToLower() == normalizedIdentifier || u.Username.ToLower() == normalizedIdentifier,
            cancellationToken: cancellationToken);

        if (user == null || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
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

        user.Status = AccountStatus.LoggedIn;
        user.LastLoginAt = DateTime.UtcNow;

        return await GenerateAuthResponseAsync(user, cancellationToken);
    }

    public async Task<AuthResponseDto> RefreshTokenAsync(RefreshTokenRequestDto request, CancellationToken cancellationToken = default)
    {
        var hashedToken = TokenHasher.Hash(request.RefreshToken);
        var userRepo = _unitOfWork.Repository<UserAccount>();

        var user = await userRepo.FirstOrDefaultAsync(
            u => u.RefreshTokenHash == hashedToken,
            cancellationToken: cancellationToken);

        if (user == null || user.RefreshTokenExpiresAt == null || user.RefreshTokenExpiresAt < DateTime.UtcNow)
        {
            throw new UnauthorizedException("Refresh token không hợp lệ hoặc đã hết hạn. Vui lòng đăng nhập lại.");
        }

        // Tài khoản bị khoá sau khi đã đăng nhập không được phép làm mới phiên.
        if (user.Status == AccountStatus.Suspended)
        {
            throw new UnauthorizedException("Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên.");
        }

        return await GenerateAuthResponseAsync(user, cancellationToken);
    }

    public async Task RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default)
    {
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
            Role = UserRole.Parent, // Mặc định người dùng đăng ký là phụ huynh
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

    public async Task LogoutAsync(int userId, CancellationToken cancellationToken = default)
    {
        var userRepo = _unitOfWork.Repository<UserAccount>();
        var user = await userRepo.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new NotFoundException("Tài khoản", userId);
        }

        user.RefreshTokenHash = null;
        user.RefreshTokenExpiresAt = null;
        // Không hạ cấp trạng thái Suspended — tài khoản bị khoá phải giữ nguyên trạng thái khoá.
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
        user.UpdatedAt = DateTime.UtcNow;

        userRepo.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthResponseDto> GenerateAuthResponseAsync(UserAccount user, CancellationToken cancellationToken)
    {
        var accessToken = _jwtTokenGenerator.GenerateAccessToken(user);
        var rawRefreshToken = _jwtTokenGenerator.GenerateRefreshToken();

        user.RefreshTokenHash = TokenHasher.Hash(rawRefreshToken);
        user.RefreshTokenExpiresAt = _jwtTokenGenerator.GetRefreshTokenExpirationDate();

        // Chỉ gọi Update() cho tài khoản ĐÃ tồn tại (Id != 0, được fetch bằng
        // FirstOrDefaultAsync AsNoTracking() nên cần Attach lại thủ công).
        // Với tài khoản MỚI (RegisterAsync, Id == 0), entity đã được AddAsync() tracking
        // sẵn ở trạng thái Added — gọi Update() lúc này sẽ đổi nhầm trạng thái thành
        // Modified và khiến EF Core phát UPDATE thay vì INSERT, gây lỗi khi lưu.
        if (user.Id != 0)
        {
            _unitOfWork.Repository<UserAccount>().Update(user);
        }

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
