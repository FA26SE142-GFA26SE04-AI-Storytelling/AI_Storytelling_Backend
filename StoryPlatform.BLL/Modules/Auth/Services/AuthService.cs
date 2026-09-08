using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using StoryPlatform.BLL.Common.Exceptions;
using StoryPlatform.BLL.Common.Security;
using StoryPlatform.BLL.Modules.Auth.DTOs;
using StoryPlatform.BLL.Modules.Auth.Interfaces;
using StoryPlatform.DAL.Entities;
using StoryPlatform.DAL.Entities.Enums;
using StoryPlatform.DAL.Interfaces;

namespace StoryPlatform.BLL.Modules.Auth.Services;

public class AuthService : IAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly JwtOptions _jwtOptions;

    public AuthService(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IOptions<JwtOptions> jwtOptions)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _jwtOptions = jwtOptions.Value;
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

        user.Status = AccountStatus.LoggedIn;
        user.LastLoginAt = DateTime.UtcNow;
        userRepo.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return GenerateAuthResponse(user);
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default)
    {
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

        var newUser = new UserAccount
        {
            Username = request.Username.Trim(),
            Email = normalizedEmail,
            FullName = request.FullName.Trim(),
            PhoneNumber = request.PhoneNumber,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            Role = UserRole.Parent, // Mặc định người dùng đăng ký là phụ huynh
            Status = AccountStatus.Registered,
            CreatedAt = DateTime.UtcNow
        };

        await userRepo.AddAsync(newUser, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return GenerateAuthResponse(newUser);
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

    private AuthResponseDto GenerateAuthResponse(UserAccount user)
    {
        var accessToken = _jwtTokenGenerator.GenerateAccessToken(user);
        var refreshToken = _jwtTokenGenerator.GenerateRefreshToken();

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            TokenType = "Bearer",
            ExpiresInSeconds = _jwtOptions.ExpiryMinutes * 60,
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
