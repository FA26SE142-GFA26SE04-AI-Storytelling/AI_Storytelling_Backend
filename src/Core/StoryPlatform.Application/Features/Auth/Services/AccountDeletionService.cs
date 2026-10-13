using System.Text.Json;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Auth.DTOs;
using StoryPlatform.Application.Features.Auth.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Sessions.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Auth.Services;

/// <summary>Xóa và ẩn danh hóa tài khoản Parent.</summary>
public class AccountDeletionService : IAccountDeletionService
{
    private const string AnonymizedName = "Tài khoản đã xóa";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISupervisionService _supervisionService;
    private readonly IChildSessionRevoker _childSessionRevoker;
    private readonly INotificationService _notificationService;
    private readonly IEmailSender _emailSender;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly ILogger<AccountDeletionService> _logger;

    public AccountDeletionService(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ISupervisionService supervisionService,
        IChildSessionRevoker childSessionRevoker,
        INotificationService notificationService,
        IEmailSender emailSender,
        IAuditLogWriter auditLogWriter,
        ILogger<AccountDeletionService> logger)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _supervisionService = supervisionService;
        _childSessionRevoker = childSessionRevoker;
        _notificationService = notificationService;
        _emailSender = emailSender;
        _auditLogWriter = auditLogWriter;
        _logger = logger;
    }

    public async Task<DeleteAccountPreviewDto> GetPreviewAsync(
        int userId, CancellationToken cancellationToken = default)
    {
        await LoadParentAsync(userId, cancellationToken);
        return BuildPreview(await LoadOwnedProfilesAsync(userId, cancellationToken));
    }

    public async Task DeleteAccountAsync(
        int userId, DeleteAccountRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var user = await LoadParentAsync(userId, cancellationToken);
        if (!_passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
        {
            throw new BadRequestException("Mật khẩu hiện tại không chính xác.");
        }

        var ownedProfiles = await LoadOwnedProfilesAsync(userId, cancellationToken);
        var preview = BuildPreview(ownedProfiles);
        if (!preview.CanDelete)
        {
            var names = string.Join(", ", preview.BlockingProfiles.Select(p => p.Nickname));
            throw new BadRequestException(
                $"Bạn vẫn là Owner của hồ sơ: {names}. Hãy chuyển quyền Owner hoặc gửi yêu cầu xóa dữ liệu của hồ sơ trước khi xóa tài khoản.");
        }

        if (preview.DraftProfiles.Count > 0 && !request.DeleteDraftProfiles)
        {
            throw new BadRequestException(
                $"Bạn còn {preview.DraftProfiles.Count} hồ sơ nháp sẽ bị xóa cùng tài khoản. Hãy xác nhận xóa các hồ sơ này để tiếp tục.");
        }

        var originalEmail = user.Email;
        var originalName = user.FullName;
        var now = DateTime.UtcNow;
        var draftProfiles = ownedProfiles
            .Where(profile => profile.Status == ChildProfileStatus.Draft)
            .ToList();
        var draftIds = draftProfiles.Select(profile => profile.Id).ToList();
        var leftProfileIds = new List<int>();

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var profileRepo = _unitOfWork.Repository<ChildProfile>();
            foreach (var draft in draftProfiles)
            {
                draft.Status = ChildProfileStatus.Deleted;
                draft.UpdatedAt = now;
                profileRepo.Update(draft);
            }

            var relationshipRepo = _unitOfWork.Repository<SupervisionRelationship>();
            var relationships = await relationshipRepo.FindAsync(
                value => value.RevokedAt == null
                         && (value.SupervisorUserId == userId
                             || draftIds.Contains(value.ChildProfileId)),
                cancellationToken: cancellationToken);
            foreach (var relationship in relationships)
            {
                if (draftIds.Contains(relationship.ChildProfileId))
                {
                    relationship.RevokedAt = now;
                    relationship.RevokedByUserId = userId;
                    relationshipRepo.Update(relationship);
                }
                else if (relationship.SupervisorRole == SupervisorRole.AdditionalSupervisor)
                {
                    await _supervisionService.ApplyRevocationAsync(
                        relationship, userId, now, cancellationToken);
                    leftProfileIds.Add(relationship.ChildProfileId);
                }
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

        foreach (var draft in draftProfiles)
        {
            await _auditLogWriter.LogAsync(
                userId, "DELETE_DRAFT_CHILD_PROFILE", nameof(ChildProfile), draft.Id,
                new { status = ChildProfileStatus.Draft.ToString() },
                new { status = ChildProfileStatus.Deleted.ToString() }, cancellationToken);
        }

        await _auditLogWriter.LogAsync(
            userId, "DELETE_ACCOUNT", nameof(UserAccount), userId,
            null,
            new
            {
                draftProfilesDeleted = draftIds.Count,
                supervisionsLeft = leftProfileIds.Count
            },
            cancellationToken);

        await NotifyOwnersAsync(userId, leftProfileIds, cancellationToken);
        await SendConfirmationEmailAsync(originalEmail, originalName, cancellationToken);
    }

    private async Task<UserAccount> LoadParentAsync(
        int userId, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(userId, cancellationToken)
                   ?? throw new NotFoundException("Tài khoản", userId);
        if (user.Role != UserRole.Parent)
        {
            throw new ForbiddenException(
                "Chỉ tài khoản Parent mới tự xóa tài khoản được.");
        }

        return user;
    }

    private async Task<List<ChildProfile>> LoadOwnedProfilesAsync(
        int userId, CancellationToken cancellationToken)
    {
        var profiles = await _unitOfWork.Repository<ChildProfile>().FindAsync(
            value => value.OwnerUserId == userId
                     && value.Status != ChildProfileStatus.Deleted,
            cancellationToken: cancellationToken);
        return profiles.ToList();
    }

    private static DeleteAccountPreviewDto BuildPreview(
        IEnumerable<ChildProfile> ownedProfiles)
    {
        var preview = new DeleteAccountPreviewDto();
        foreach (var profile in ownedProfiles)
        {
            var summary = new OwnedChildProfileSummaryDto
            {
                Id = profile.Id,
                Nickname = profile.Nickname,
                Status = profile.Status.ToString()
            };

            if (profile.Status == ChildProfileStatus.Draft)
            {
                preview.DraftProfiles.Add(summary);
            }
            else
            {
                preview.BlockingProfiles.Add(summary);
            }
        }

        return preview;
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
        user.MfaSecret = null;
        user.MfaEnabled = false;
        user.TokenVersion += 1;
        user.IsDeleted = true;
        user.UpdatedAt = now;
    }

    private async Task NotifyOwnersAsync(
        int userId, IEnumerable<int> leftProfileIds,
        CancellationToken cancellationToken)
    {
        foreach (var childProfileId in leftProfileIds.Distinct())
        {
            var profile = await _unitOfWork.Repository<ChildProfile>()
                .GetByIdAsync(childProfileId, cancellationToken);
            if (profile == null)
            {
                continue;
            }

            await _notificationService.CreateAsync(
                profile.OwnerUserId, NotificationType.SupervisionRevoked,
                JsonSerializer.Serialize(new
                {
                    childProfileId,
                    revokedByUserId = userId,
                    reason = "account_deleted"
                }),
                cancellationToken);
        }
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
