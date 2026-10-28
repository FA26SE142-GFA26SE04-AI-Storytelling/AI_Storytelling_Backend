using System.Security.Cryptography;
using System.Text;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Security;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Auth.DTOs;
using StoryPlatform.Application.Features.Auth.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Auth.Services;

public class StaffActivationService : IStaffActivationService
{
    private const string InvalidInviteMessage = "Mã mời không hợp lệ hoặc đã hết hạn.";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditLogWriter _auditLogWriter;

    public StaffActivationService(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IAuditLogWriter auditLogWriter)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ActivateAsync(
        StaffActivationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!request.CommitmentAccepted)
        {
            throw new BadRequestException(
                "Bạn cần xác nhận cam kết bảo mật dữ liệu trẻ em và quy tắc kiểm duyệt.");
        }

        if (request.NewPassword != request.ConfirmPassword)
        {
            throw new BadRequestException("Mật khẩu xác nhận không khớp.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var userRepository = _unitOfWork.Repository<UserAccount>();
        var user = await userRepository.FirstOrDefaultAsync(
            account => account.Email.ToLower() == normalizedEmail,
            cancellationToken: cancellationToken);

        if (!HasValidInvite(user, request.InviteToken))
        {
            throw new BadRequestException(InvalidInviteMessage);
        }

        var now = DateTime.UtcNow;
        user!.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.PasswordChangedAt = now;
        user.CommitmentAcceptedAt = now;
        user.EmailVerifiedAt = now;
        user.Status = AccountStatus.Active;
        user.ResetTokenHash = null;
        user.ResetTokenExpiresAt = null;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.TokenVersion += 1;
        user.UpdatedAt = now;
        userRepository.Update(user);

        await _auditLogWriter.LogAsync(
            user.Id, user.Role == UserRole.Administrator ? "AdministratorActivated" : "ModeratorActivated",
            nameof(UserAccount), user.Id,
            beforeState: new { status = AccountStatus.PasswordResetPending.ToString() },
            afterState: new { status = AccountStatus.Active.ToString() },
            cancellationToken: cancellationToken);
    }

    private static bool HasValidInvite(UserAccount? user, string inviteToken)
    {
        if (user == null
            || user.Role is not (UserRole.Moderator or UserRole.Administrator)
            || user.Status != AccountStatus.PasswordResetPending
            || user.CommitmentAcceptedAt != null
            || user.ResetTokenHash == null
            || user.ResetTokenExpiresAt == null
            || user.ResetTokenExpiresAt <= DateTime.UtcNow)
        {
            return false;
        }

        var expectedHash = Encoding.UTF8.GetBytes(user.ResetTokenHash);
        var providedHash = Encoding.UTF8.GetBytes(TokenHasher.Hash(inviteToken.Trim()));
        return expectedHash.Length == providedHash.Length
               && CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }
}
