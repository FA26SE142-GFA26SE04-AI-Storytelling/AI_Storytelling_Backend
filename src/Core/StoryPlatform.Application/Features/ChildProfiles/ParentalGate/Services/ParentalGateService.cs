using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.ParentalGate.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.ParentalGate.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.ParentalGate.Services;

public class ParentalGateService : IParentalGateService
{
    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;

    public ParentalGateService(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
    }

    public async Task VerifyAsync(
        VerifyParentalGateRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var userRepo = _unitOfWork.Repository<UserAccount>();
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await userRepo.FirstOrDefaultAsync(
            account => account.Email.ToLower() == normalizedEmail,
            cancellationToken: cancellationToken);

        if (user == null)
        {
            throw new BadRequestException("Email hoặc mật khẩu không chính xác.");
        }

        // Reuse the same lockout fields as normal login so this endpoint cannot be used as an
        // unlimited password-guessing path.
        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
        {
            throw new ForbiddenException(
                $"Tài khoản tạm thời bị khoá do nhập sai quá {MaxFailedAttempts} lần. "
                + $"Vui lòng thử lại sau {LockoutMinutes} phút.");
        }

        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            user.FailedLoginAttempts += 1;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
                user.FailedLoginAttempts = 0;
            }

            userRepo.Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new BadRequestException("Email hoặc mật khẩu không chính xác.");
        }

        if (user.Status == AccountStatus.Suspended)
        {
            throw new ForbiddenException(
                "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên.");
        }

        // Successful gate verification does not create tokens or alter account/login state.
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        userRepo.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
