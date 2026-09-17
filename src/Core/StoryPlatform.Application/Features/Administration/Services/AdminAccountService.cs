using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Administration.DTOs;
using StoryPlatform.Application.Features.Administration.Interfaces;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Administration.Services;

public class AdminAccountService : IAdminAccountService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INotificationService _notificationService;

    public AdminAccountService(
        IUnitOfWork unitOfWork, IAuditLogWriter auditLogWriter, INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _notificationService = notificationService;
    }

    public async Task<AdministratorAccountDto> GrantAsync(
        int actorAdminUserId, GrantAdministratorRequestDto request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var userRepository = _unitOfWork.Repository<UserAccount>();
        var target = await userRepository.FirstOrDefaultAsync(
            user => user.Email.ToLower() == normalizedEmail, cancellationToken: cancellationToken);
        if (target == null)
        {
            throw new NotFoundException("Tài khoản", request.Email);
        }

        if (target.Role == UserRole.Administrator)
        {
            throw new ConflictException("Tài khoản này đã là Administrator.");
        }

        var hasSchoolAdminConflict = await _unitOfWork.Repository<OrganizationMembership>().ExistsAsync(
            membership => membership.UserId == target.Id
                          && membership.Status == MembershipStatus.Active
                          && membership.OrgRole == OrgRole.SchoolAdmin,
            cancellationToken);
        if (hasSchoolAdminConflict && !request.ConfirmSchoolAdminConflict)
        {
            throw new ConflictException(
                "Tài khoản đang là School Admin đang active của một Organization. " +
                "Xác nhận lại (confirmSchoolAdminConflict=true) nếu vẫn muốn cấp quyền Administrator.");
        }

        var beforeRole = target.Role;
        target.RoleBeforeAdmin = beforeRole;
        target.Role = UserRole.Administrator;
        target.UpdatedAt = DateTime.UtcNow;
        userRepository.Update(target);

        await _auditLogWriter.LogAsync(
            actorAdminUserId, "AdminGranted", nameof(UserAccount), target.Id,
            beforeState: new { role = beforeRole.ToString() },
            afterState: new { role = UserRole.Administrator.ToString() },
            cancellationToken: cancellationToken);

        await _notificationService.CreateAsync(
            target.Id, NotificationType.AdminRoleChanged,
            JsonSerializer.Serialize(new { action = "granted" }), cancellationToken);

        return MapToDto(target);
    }

    public async Task<AdministratorAccountDto> RevokeAsync(
        int actorAdminUserId, int targetUserId, CancellationToken cancellationToken = default)
    {
        var userRepository = _unitOfWork.Repository<UserAccount>();
        var target = await userRepository.GetByIdAsync(targetUserId, cancellationToken);
        if (target == null)
        {
            throw new NotFoundException("Tài khoản", targetUserId);
        }

        if (target.Role != UserRole.Administrator)
        {
            throw new BadRequestException("Tài khoản không phải Administrator.");
        }

        var activeAdminCount = await userRepository.CountAsync(
            user => user.Role == UserRole.Administrator && user.Status != AccountStatus.Suspended,
            cancellationToken);
        if (activeAdminCount <= 1)
        {
            throw new ConflictException("Không thể thu hồi quyền của Administrator cuối cùng đang active.");
        }

        var restoredRole = target.RoleBeforeAdmin ?? UserRole.Teacher;
        target.Role = restoredRole;
        target.RoleBeforeAdmin = null;
        target.UpdatedAt = DateTime.UtcNow;
        userRepository.Update(target);

        await _auditLogWriter.LogAsync(
            actorAdminUserId, "AdminRevoked", nameof(UserAccount), target.Id,
            beforeState: new { role = UserRole.Administrator.ToString() },
            afterState: new { role = restoredRole.ToString() },
            cancellationToken: cancellationToken);

        await _notificationService.CreateAsync(
            target.Id, NotificationType.AdminRoleChanged,
            JsonSerializer.Serialize(new { action = "revoked" }), cancellationToken);

        return MapToDto(target);
    }

    private static AdministratorAccountDto MapToDto(UserAccount user) => new()
    {
        UserId = user.Id,
        Email = user.Email,
        FullName = user.FullName,
        Role = user.Role.ToString(),
        RoleBeforeAdmin = user.RoleBeforeAdmin?.ToString()
    };
}
