using System.Linq.Expressions;
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

public class StaffAccountService : IStaffAccountService
{
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromHours(72);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserProvisioningService _userProvisioningService;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IEmailSender _emailSender;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INotificationService _notificationService;
    private readonly IModerationSweepService _sweepService;

    public StaffAccountService(
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

    public async Task<List<StaffAccountDto>> ListAsync(
        UserRole? role, CancellationToken cancellationToken = default)
    {
        if (role.HasValue && !IsStaffRole(role.Value))
        {
            throw new BadRequestException("Chỉ lọc được theo vai trò Moderator hoặc Administrator.");
        }

        Expression<Func<UserAccount, bool>> filter = role is { } onlyRole
            ? user => user.Role == onlyRole
            : user => user.Role == UserRole.Moderator || user.Role == UserRole.Administrator;
        var accounts = await _unitOfWork.Repository<UserAccount>().FindAsync(
            filter, cancellationToken: cancellationToken);

        return accounts
            .OrderByDescending(user => user.CreatedAt)
            .Select(MapToDto)
            .ToList();
    }

    public async Task<StaffAccountDto> InviteAsync(
        int actorAdminUserId, InviteStaffRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.Role is not { } role || !IsStaffRole(role))
        {
            throw new BadRequestException("Vai trò phải là Moderator hoặc Administrator.");
        }

        if (role == UserRole.Moderator && string.IsNullOrWhiteSpace(request.Expertise))
        {
            throw new BadRequestException("Chuyên môn không được để trống khi mời Moderator.");
        }

        var actor = await RequireActorAsync(actorAdminUserId, cancellationToken);
        var email = request.Email.Trim().ToLowerInvariant();
        var usernamePrefix = role == UserRole.Moderator ? "mod_" : "admin_";
        var username = usernamePrefix + Guid.NewGuid().ToString("N")[..16];

        var (account, rawInviteToken) = await _userProvisioningService.CreatePendingAccountAsync(
            username, email, request.FullName, null, role,
            InviteLifetime, cancellationToken);
        account.Expertise = role == UserRole.Moderator ? request.Expertise!.Trim() : null;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            actor.Id, $"{role}Invited", nameof(UserAccount), account.Id,
            beforeState: null,
            afterState: new { email, role = role.ToString(), expertise = account.Expertise, note = request.Note?.Trim() },
            cancellationToken: cancellationToken);

        await _emailSender.SendStaffInviteEmailAsync(
            account.Email, account.FullName, actor.FullName, RoleLabel(role),
            rawInviteToken, cancellationToken);

        return MapToDto(account);
    }

    public async Task<StaffAccountDto> ResendInviteAsync(
        int actorAdminUserId, int targetUserId,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync(actorAdminUserId, cancellationToken);
        var target = await RequireStaffAsync(targetUserId, cancellationToken);
        if (!IsPendingInvite(target))
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
            actor.Id, $"{target.Role}InviteResent", nameof(UserAccount), target.Id,
            beforeState: null,
            afterState: new { inviteExpiresAt = target.ResetTokenExpiresAt },
            cancellationToken: cancellationToken);

        await _emailSender.SendStaffInviteEmailAsync(
            target.Email, target.FullName, actor.FullName, RoleLabel(target.Role),
            rawInviteToken, cancellationToken);

        return MapToDto(target);
    }

    public async Task<StaffAccountDto> CancelInviteAsync(
        int actorAdminUserId, int targetUserId,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync(actorAdminUserId, cancellationToken);
        var target = await RequireStaffAsync(targetUserId, cancellationToken);
        if (!IsPendingInvite(target))
        {
            throw new ConflictException(
                "Tài khoản này đã được kích hoạt; hãy dùng vô hiệu hóa thay vì hủy lời mời.");
        }

        // Xóa hẳn (không xóa mềm) vì kiểm trùng email khi mời tính cả tài khoản đã xóa mềm.
        var snapshot = MapToDto(target);
        _unitOfWork.Repository<UserAccount>().Delete(target);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            actor.Id, $"{target.Role}InviteCancelled", nameof(UserAccount), target.Id,
            beforeState: new
            {
                email = target.Email,
                expertise = target.Expertise,
                inviteExpiresAt = target.ResetTokenExpiresAt
            },
            afterState: null,
            cancellationToken: cancellationToken);

        return snapshot;
    }

    public async Task<StaffAccountDto> DeactivateAsync(
        int actorAdminUserId, int targetUserId, string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3)
        {
            throw new BadRequestException("Lý do vô hiệu hóa phải có ít nhất 3 ký tự.");
        }

        var actor = await RequireActorAsync(actorAdminUserId, cancellationToken);
        var target = await RequireStaffAsync(targetUserId, cancellationToken);
        if (actor.Id == target.Id)
        {
            throw new ConflictException("Không thể tự vô hiệu hóa tài khoản của mình.");
        }

        if (target.Status == AccountStatus.PasswordResetPending)
        {
            throw new BadRequestException(
                "Tài khoản chưa kích hoạt. Hãy hủy lời mời thay vì vô hiệu hóa.");
        }

        if (target.Status == AccountStatus.Suspended)
        {
            throw new ConflictException("Tài khoản này đã bị vô hiệu hóa.");
        }

        if (target.Status != AccountStatus.Active)
        {
            throw new ConflictException("Chỉ vô hiệu hóa được tài khoản đang hoạt động.");
        }

        if (target.Role == UserRole.Administrator)
        {
            var activeAdminCount = await _unitOfWork.Repository<UserAccount>().CountAsync(
                user => user.Role == UserRole.Administrator && user.Status == AccountStatus.Active,
                cancellationToken);
            if (activeAdminCount <= 1)
            {
                throw new ConflictException("Không thể vô hiệu hóa Administrator cuối cùng đang hoạt động.");
            }
        }

        target.Status = AccountStatus.Suspended;
        target.TokenVersion += 1;
        target.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<UserAccount>().Update(target);
        await RevokeRefreshTokensAsync(target.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            actor.Id, $"{target.Role}Deactivated", nameof(UserAccount), target.Id,
            beforeState: new { status = AccountStatus.Active.ToString() },
            afterState: new { status = AccountStatus.Suspended.ToString(), reason = reason.Trim() },
            cancellationToken: cancellationToken);
        await _notificationService.CreateAsync(
            target.Id, NotificationType.AdminRoleChanged,
            JsonSerializer.Serialize(new { action = "deactivated" }), cancellationToken);

        // Administrator cũng nhận được mục kiểm duyệt, nên cả hai vai trò đều nhả việc đang giữ.
        await _sweepService.ReleaseClaimsOfModeratorAsync(target.Id, cancellationToken);
        await _sweepService.WithdrawRoadmapReviewsOfInactiveCreatorsAsync(target.Id, cancellationToken);

        return MapToDto(target);
    }

    public async Task<StaffAccountDto> ReactivateAsync(
        int actorAdminUserId, int targetUserId,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync(actorAdminUserId, cancellationToken);
        var target = await RequireStaffAsync(targetUserId, cancellationToken);
        if (target.Status != AccountStatus.Suspended)
        {
            throw new ConflictException(
                "Chỉ kích hoạt lại được tài khoản đã bị vô hiệu hóa.");
        }

        target.Status = AccountStatus.Active;
        target.FailedLoginAttempts = 0;
        target.LockedUntil = null;
        target.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<UserAccount>().Update(target);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            actor.Id, $"{target.Role}Reactivated", nameof(UserAccount), target.Id,
            beforeState: new { status = AccountStatus.Suspended.ToString() },
            afterState: new { status = AccountStatus.Active.ToString() },
            cancellationToken: cancellationToken);
        await _notificationService.CreateAsync(
            target.Id, NotificationType.AdminRoleChanged,
            JsonSerializer.Serialize(new { action = "reactivated" }), cancellationToken);

        return MapToDto(target);
    }

    private static bool IsStaffRole(UserRole role) =>
        role is UserRole.Moderator or UserRole.Administrator;

    private static bool IsPendingInvite(UserAccount user) =>
        user.Status == AccountStatus.PasswordResetPending && user.CommitmentAcceptedAt == null;

    private static string RoleLabel(UserRole role) => role == UserRole.Administrator
        ? "Quản trị viên (Administrator)"
        : "Kiểm duyệt viên (Moderator)";

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
                "Chỉ Administrator đang hoạt động mới được quản lý tài khoản vận hành.");
        }

        return actor;
    }

    private async Task<UserAccount> RequireStaffAsync(
        int targetUserId, CancellationToken cancellationToken)
    {
        var target = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(targetUserId, cancellationToken);
        if (target == null || !IsStaffRole(target.Role))
        {
            throw new NotFoundException("Tài khoản Moderator / Administrator", targetUserId);
        }

        return target;
    }

    private static StaffAccountDto MapToDto(UserAccount user) => new()
    {
        UserId = user.Id,
        Email = user.Email,
        FullName = user.FullName,
        Role = user.Role.ToString(),
        Expertise = user.Expertise,
        Status = user.Status.ToString(),
        IsActivated = user.Status != AccountStatus.PasswordResetPending,
        ActivatedAt = user.CommitmentAcceptedAt,
        LastLoginAt = user.LastLoginAt,
        InviteExpiresAt = user.Status == AccountStatus.PasswordResetPending
            ? user.ResetTokenExpiresAt
            : null
    };
}
