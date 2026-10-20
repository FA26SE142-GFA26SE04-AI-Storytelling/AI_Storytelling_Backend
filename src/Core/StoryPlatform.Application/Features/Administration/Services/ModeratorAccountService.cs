using System.Text.Json;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Security;
using StoryPlatform.Application.Features.Administration.DTOs;
using StoryPlatform.Application.Features.Administration.Interfaces;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Auth.Interfaces;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Administration.Services;

public class ModeratorAccountService : IModeratorAccountService
{
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromHours(72);
    private const string ModeratorRoleLabel = "Kiểm duyệt viên (Moderator)";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserProvisioningService _userProvisioningService;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IEmailSender _emailSender;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INotificationService _notificationService;
    private readonly IModerationSweepService _sweepService;

    public ModeratorAccountService(
        IUnitOfWork unitOfWork,
        IUserProvisioningService userProvisioningService,
        IJwtTokenGenerator jwtTokenGenerator,
        IEmailSender emailSender,
        IAuditLogWriter auditLogWriter,
        INotificationService notificationService,
        IModerationSweepService sweepService)
    {
        _unitOfWork = unitOfWork;
        _userProvisioningService = userProvisioningService;
        _jwtTokenGenerator = jwtTokenGenerator;
        _emailSender = emailSender;
        _auditLogWriter = auditLogWriter;
        _notificationService = notificationService;
        _sweepService = sweepService;
    }

    public async Task<ModeratorAccountDto> InviteAsync(
        int actorAdminUserId, InviteModeratorRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync(actorAdminUserId, cancellationToken);
        var email = request.Email.Trim().ToLowerInvariant();
        var username = "mod_" + Guid.NewGuid().ToString("N")[..16];

        var (account, rawInviteToken) = await _userProvisioningService.CreatePendingAccountAsync(
            username, email, request.FullName, null, UserRole.Moderator,
            InviteLifetime, cancellationToken);
        account.Expertise = request.Expertise.Trim();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            actor.Id, "ModeratorInvited", nameof(UserAccount), account.Id,
            beforeState: null,
            afterState: new { email, expertise = account.Expertise, note = request.Note?.Trim() },
            cancellationToken: cancellationToken);

        await _emailSender.SendStaffInviteEmailAsync(
            account.Email, account.FullName, actor.FullName, ModeratorRoleLabel,
            rawInviteToken, cancellationToken);

        return MapToDto(account);
    }

    public async Task<ModeratorAccountDto> ResendInviteAsync(
        int actorAdminUserId, int targetUserId,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync(actorAdminUserId, cancellationToken);
        var target = await RequireModeratorAsync(targetUserId, cancellationToken);
        if (target.CommitmentAcceptedAt != null || target.Status != AccountStatus.PasswordResetPending)
        {
            throw new ConflictException(
                "Tài khoản này đã được kích hoạt hoặc không còn ở trạng thái chờ kích hoạt.");
        }

        var rawInviteToken = _jwtTokenGenerator.GenerateRefreshToken();
        target.ResetTokenHash = TokenHasher.Hash(rawInviteToken);
        target.ResetTokenExpiresAt = DateTime.UtcNow.Add(InviteLifetime);
        target.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<UserAccount>().Update(target);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            actor.Id, "ModeratorInviteResent", nameof(UserAccount), target.Id,
            beforeState: null,
            afterState: new { inviteExpiresAt = target.ResetTokenExpiresAt },
            cancellationToken: cancellationToken);

        await _emailSender.SendStaffInviteEmailAsync(
            target.Email, target.FullName, actor.FullName, ModeratorRoleLabel,
            rawInviteToken, cancellationToken);

        return MapToDto(target);
    }

    public async Task<List<ModeratorAccountDto>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var moderators = await _unitOfWork.Repository<UserAccount>().FindAsync(
            user => user.Role == UserRole.Moderator,
            cancellationToken: cancellationToken);

        return moderators
            .OrderByDescending(user => user.CreatedAt)
            .Select(MapToDto)
            .ToList();
    }

    public async Task<ModeratorAccountDto> DeactivateAsync(
        int actorAdminUserId, int targetUserId, string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3)
        {
            throw new BadRequestException("Lý do vô hiệu hóa phải có ít nhất 3 ký tự.");
        }

        var actor = await RequireActorAsync(actorAdminUserId, cancellationToken);
        var target = await RequireModeratorAsync(targetUserId, cancellationToken);
        if (target.CommitmentAcceptedAt == null)
        {
            throw new BadRequestException(
                "Tài khoản chưa kích hoạt. Hãy để lời mời hết hạn thay vì vô hiệu hóa.");
        }

        if (target.Status == AccountStatus.Suspended)
        {
            throw new ConflictException("Tài khoản này đã bị vô hiệu hóa.");
        }

        var beforeStatus = target.Status;
        target.Status = AccountStatus.Suspended;
        target.TokenVersion += 1;
        target.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<UserAccount>().Update(target);
        await RevokeRefreshTokensAsync(target.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            actor.Id, "ModeratorDeactivated", nameof(UserAccount), target.Id,
            beforeState: new { status = beforeStatus.ToString() },
            afterState: new { status = AccountStatus.Suspended.ToString(), reason = reason.Trim() },
            cancellationToken: cancellationToken);
        await _notificationService.CreateAsync(
            target.Id, NotificationType.AdminRoleChanged,
            JsonSerializer.Serialize(new { action = "deactivated" }), cancellationToken);

        await _sweepService.ReleaseClaimsOfModeratorAsync(target.Id, cancellationToken);
        await _sweepService.WithdrawRoadmapReviewsOfInactiveCreatorsAsync(target.Id, cancellationToken);

        return MapToDto(target);
    }

    public async Task<ModeratorAccountDto> ReactivateAsync(
        int actorAdminUserId, int targetUserId,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync(actorAdminUserId, cancellationToken);
        var target = await RequireModeratorAsync(targetUserId, cancellationToken);
        if (target.Status != AccountStatus.Suspended || target.CommitmentAcceptedAt == null)
        {
            throw new ConflictException(
                "Chỉ kích hoạt lại được tài khoản Moderator đã bị vô hiệu hóa.");
        }

        target.Status = AccountStatus.Active;
        target.FailedLoginAttempts = 0;
        target.LockedUntil = null;
        target.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<UserAccount>().Update(target);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            actor.Id, "ModeratorReactivated", nameof(UserAccount), target.Id,
            beforeState: new { status = AccountStatus.Suspended.ToString() },
            afterState: new { status = AccountStatus.Active.ToString() },
            cancellationToken: cancellationToken);
        await _notificationService.CreateAsync(
            target.Id, NotificationType.AdminRoleChanged,
            JsonSerializer.Serialize(new { action = "reactivated" }), cancellationToken);

        return MapToDto(target);
    }

    private async Task RevokeRefreshTokensAsync(
        int userId, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.Repository<RefreshToken>();
        var sessions = await repository.FindAsync(
            token => token.UserAccountId == userId && token.RevokedAt == null,
            cancellationToken: cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var session in sessions)
        {
            session.RevokedAt = now;
            repository.Update(session);
        }
    }

    private async Task<UserAccount> RequireActorAsync(
        int actorAdminUserId, CancellationToken cancellationToken)
    {
        var actor = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(actorAdminUserId, cancellationToken);
        if (actor == null || actor.Role != UserRole.Administrator
            || actor.Status != AccountStatus.Active)
        {
            throw new ForbiddenException(
                "Chỉ Administrator đang hoạt động mới được quản lý tài khoản Moderator.");
        }

        return actor;
    }

    private async Task<UserAccount> RequireModeratorAsync(
        int targetUserId, CancellationToken cancellationToken)
    {
        var target = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(targetUserId, cancellationToken);
        if (target == null || target.Role != UserRole.Moderator)
        {
            throw new NotFoundException("Moderator", targetUserId);
        }

        return target;
    }

    private static ModeratorAccountDto MapToDto(UserAccount user) => new()
    {
        UserId = user.Id,
        Email = user.Email,
        FullName = user.FullName,
        Expertise = user.Expertise,
        Status = user.Status.ToString(),
        IsActivated = user.CommitmentAcceptedAt != null,
        ActivatedAt = user.CommitmentAcceptedAt,
        LastLoginAt = user.LastLoginAt,
        InviteExpiresAt = user.Status == AccountStatus.PasswordResetPending
            ? user.ResetTokenExpiresAt
            : null
    };
}
