using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Auth.DTOs;
using StoryPlatform.Application.Features.Auth.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Sessions.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Auth.Services;

/// <summary>Xóa và ẩn danh hóa tài khoản User.</summary>
public class AccountDeletionService : IAccountDeletionService
{
    private const string AnonymizedName = "Tài khoản đã xóa";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IChildProfileEraser _childProfileEraser;
    private readonly IChildSessionRevoker _childSessionRevoker;
    private readonly IEmailSender _emailSender;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly ILogger<AccountDeletionService> _logger;

    public AccountDeletionService(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IChildProfileEraser childProfileEraser,
        IChildSessionRevoker childSessionRevoker,
        IEmailSender emailSender,
        IAuditLogWriter auditLogWriter,
        ILogger<AccountDeletionService> logger)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _childProfileEraser = childProfileEraser;
        _childSessionRevoker = childSessionRevoker;
        _emailSender = emailSender;
        _auditLogWriter = auditLogWriter;
        _logger = logger;
    }

    public async Task<DeleteAccountPreviewDto> GetPreviewAsync(
        int userId, CancellationToken cancellationToken = default)
    {
        await LoadUserAsync(userId, cancellationToken);
        return BuildPreview(await LoadOwnedProfilesAsync(userId, cancellationToken));
    }

    public async Task DeleteAccountAsync(
        int userId, DeleteAccountRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var user = await LoadUserAsync(userId, cancellationToken);
        if (!_passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
        {
            throw new BadRequestException("Mật khẩu hiện tại không chính xác.");
        }

        var ownedProfiles = await LoadOwnedProfilesAsync(userId, cancellationToken);
        if (ownedProfiles.Count > 0 && !request.DeleteChildProfiles)
        {
            throw new BadRequestException(
                $"Bạn còn {ownedProfiles.Count} hồ sơ trẻ sẽ bị xóa cùng tài khoản. Hãy xác nhận xóa các hồ sơ này để tiếp tục.");
        }

        var originalEmail = user.Email;
        var originalName = user.FullName;
        var now = DateTime.UtcNow;
        var erased = ownedProfiles.Select(profile => new { profile.Id, Status = profile.Status.ToString() }).ToList();

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var profile in ownedProfiles)
            {
                await _childProfileEraser.EraseAsync(profile, userId, now, cancellationToken);
            }

            var paymentRepo = _unitOfWork.Repository<PaymentTransaction>();
            var pendingPayments = await paymentRepo.FindAsync(
                value => value.PayerUserId == userId
                         && value.Status == PaymentStatus.Pending,
                cancellationToken: cancellationToken);
            foreach (var payment in pendingPayments)
            {
                payment.Status = PaymentStatus.Expired;
                payment.UpdatedAt = now;
                paymentRepo.Update(payment);
            }

            var refreshRepo = _unitOfWork.Repository<RefreshToken>();
            var tokens = await refreshRepo.FindAsync(
                value => value.UserAccountId == userId && value.RevokedAt == null,
                cancellationToken: cancellationToken);
            foreach (var token in tokens)
            {
                token.RevokedAt = now;
                refreshRepo.Update(token);
            }

            await _childSessionRevoker.RevokeBySupervisorAsync(
                userId, null, cancellationToken);

            Anonymize(user, now);
            _unitOfWork.Repository<UserAccount>().Update(user);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        foreach (var profile in erased)
        {
            await _auditLogWriter.LogAsync(
                userId, "DELETE_CHILD_PROFILE_WITH_ACCOUNT", nameof(ChildProfile), profile.Id,
                new { status = profile.Status },
                new { status = ChildProfileStatus.Deleted.ToString() }, cancellationToken);
        }

        await _auditLogWriter.LogAsync(
            userId, "DELETE_ACCOUNT", nameof(UserAccount), userId,
            null, new { childProfilesDeleted = erased.Count }, cancellationToken);

        await SendConfirmationEmailAsync(originalEmail, originalName, cancellationToken);
    }

    private async Task<UserAccount> LoadUserAsync(
        int userId, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(userId, cancellationToken)
                   ?? throw new NotFoundException("Tài khoản", userId);
        if (user.Role != UserRole.User)
        {
            throw new ForbiddenException(
                "Chỉ tài khoản User mới tự xóa tài khoản được.");
        }

        return user;
    }

    private async Task<List<ChildProfile>> LoadOwnedProfilesAsync(
        int userId, CancellationToken cancellationToken)
    {
        var profiles = await _unitOfWork.Repository<ChildProfile>().FindAsync(
            value => value.OwnerUserId == userId
                     && value.Status != ChildProfileStatus.Deleted && !value.IsDeleted,
            cancellationToken: cancellationToken);
        return profiles.ToList();
    }

    private static DeleteAccountPreviewDto BuildPreview(
        IEnumerable<ChildProfile> ownedProfiles)
    {
        return new DeleteAccountPreviewDto
        {
            ChildProfiles = ownedProfiles.Select(profile => new OwnedChildProfileSummaryDto
            {
                Id = profile.Id,
                Nickname = profile.Nickname,
                Status = profile.Status.ToString()
            }).ToList()
        };
    }

    private void Anonymize(UserAccount user, DateTime now)
    {
        user.FullName = AnonymizedName;
        user.Email = $"deleted-{user.Id}@deleted.invalid";
        user.Username = $"deleted-{user.Id}";
        user.PhoneNumber = null;
        user.AvatarUrl = null;
        user.PasswordHash = _passwordHasher.HashPassword(Guid.NewGuid().ToString("N"));
        user.ResetTokenHash = null;
        user.ResetTokenExpiresAt = null;
        user.RefreshTokenHash = null;
        user.RefreshTokenExpiresAt = null;
        user.EmailVerificationTokenHash = null;
        user.EmailVerificationTokenExpiresAt = null;
        user.EmailVerificationFailedAttempts = 0;
        user.PendingEmail = null;
        user.PendingEmailOtpHash = null;
        user.PendingEmailOtpExpiresAt = null;
        user.PendingEmailOtpFailedAttempts = 0;
        user.TokenVersion += 1;
        user.IsDeleted = true;
        user.UpdatedAt = now;
    }

    private async Task SendConfirmationEmailAsync(
        string email, string name, CancellationToken cancellationToken)
    {
        try
        {
            await _emailSender.SendAccountSecurityChangedEmailAsync(
                email, name, AccountSecurityChange.AccountDeleted, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception, "Không gửi được email xác nhận xóa tài khoản.");
        }
    }
}
