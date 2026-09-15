using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Security;
using StoryPlatform.Application.Features.Auth.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Auth.Services;

public class UserProvisioningService : IUserProvisioningService
{
    private const int SetPasswordTokenExpiryDays = 7;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public UserProvisioningService(
        IUnitOfWork unitOfWork, IPasswordHasher passwordHasher, IJwtTokenGenerator jwtTokenGenerator)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<(UserAccount Account, string RawSetPasswordToken)> CreatePendingAccountAsync(
        string username, string email, string fullName, string? phoneNumber, UserRole role,
        CancellationToken cancellationToken = default)
    {
        var userRepo = _unitOfWork.Repository<UserAccount>();

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var emailExists = await userRepo.ExistsAsync(
            user => user.Email.ToLower() == normalizedEmail, cancellationToken);
        if (emailExists)
        {
            throw new BadRequestException($"Email '{email}' đã được sử dụng trong hệ thống.");
        }

        var normalizedUsername = username.Trim().ToLowerInvariant();
        var usernameExists = await userRepo.ExistsAsync(
            user => user.Username.ToLower() == normalizedUsername, cancellationToken);
        if (usernameExists)
        {
            throw new BadRequestException($"Tên đăng nhập '{username}' đã tồn tại.");
        }

        // The random initial password is discarded. The account can only become usable after
        // its owner consumes the separately generated set-password token.
        var randomInitialPassword = _jwtTokenGenerator.GenerateRefreshToken();
        var rawSetPasswordToken = _jwtTokenGenerator.GenerateRefreshToken();

        var account = new UserAccount
        {
            Username = username.Trim(),
            Email = normalizedEmail,
            FullName = fullName.Trim(),
            PhoneNumber = phoneNumber,
            PasswordHash = _passwordHasher.HashPassword(randomInitialPassword),
            Role = role,
            Status = AccountStatus.PasswordResetPending,
            ResetTokenHash = TokenHasher.Hash(rawSetPasswordToken),
            ResetTokenExpiresAt = DateTime.UtcNow.AddDays(SetPasswordTokenExpiryDays),
            CreatedAt = DateTime.UtcNow
        };

        await userRepo.AddAsync(account, cancellationToken);
        return (account, rawSetPasswordToken);
    }
}
