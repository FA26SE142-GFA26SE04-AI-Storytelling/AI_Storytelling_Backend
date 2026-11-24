using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AccessControl.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.AccessControl.Services;

/// <inheritdoc />
public sealed class AccessPolicy(IUnitOfWork uow, IPermissionService permissions) : IAccessPolicy
{
    public const string DefaultDeniedMessage = "Bạn không có quyền thực hiện thao tác này.";
    public const string OwnContentMessage = "Bạn không thể duyệt nội dung do chính mình tạo hoặc nộp.";
    public const string AdminFallbackMessage =
        "Quản trị viên chỉ xử lý hàng đợi khi không còn kiểm duyệt viên hợp lệ để nhận mục này.";
    public const string NoActiveModeratorMessage =
        "Quản trị viên chỉ sửa cấu hình kiểm duyệt khi không còn kiểm duyệt viên đang hoạt động.";

    /// <summary>Bản dùng khi service được dựng tay (test): quyền lấy theo bản gán mặc định trong code.</summary>
    public static IAccessPolicy Default(IUnitOfWork uow) => new AccessPolicy(uow, new StaticPermissionService());

    public async Task<UserAccount> EnsureAsync(int actorUserId, string permission, object? resource = null,
        AccessMode mode = AccessMode.Write, string? deniedMessage = null, CancellationToken cancellationToken = default)
    {
        var denied = deniedMessage ?? DefaultDeniedMessage;
        var actor = await uow.Repository<UserAccount>().GetByIdAsync(actorUserId, cancellationToken);
        if (actor is null || actor.IsDeleted || actor.Status != AccountStatus.Active || actor.LockedUntil > DateTime.UtcNow)
            throw new ForbiddenException(denied);

        var roleCode = actor.Role.ToString();
        if (!await permissions.RoleHasAsync(roleCode, permission, cancellationToken))
            throw new ForbiddenException(denied);

        if (mode == AccessMode.Read) return actor;

        foreach (var condition in DefaultRolePermissions.ConditionsFor(roleCode, permission))
        {
            var failure = await EvaluateAsync(condition, actor, permission, resource, cancellationToken);
            if (failure is not null) throw new ForbiddenException(failure);
        }

        return actor;
    }

    /// <returns>Message khi điều kiện không đạt; null khi đạt.</returns>
    private async Task<string?> EvaluateAsync(string condition, UserAccount actor, string permission, object? resource,
        CancellationToken ct)
    {
        switch (condition)
        {
            case AccessConditions.NotOwnContent:
                return resource is ModerationItem own && own.CreatedByUserId == actor.Id ? OwnContentMessage : null;

            case AccessConditions.AdminFallback:
                // Chỉ xét lúc nhận mục: mục đã nhận hợp lệ thì làm tiếp được dù có kiểm duyệt viên mới hoạt động.
                if (actor.Role != UserRole.Administrator
                    || resource is not ModerationItem { Status: ModerationItemStatus.Pending } item
                    || item.EscalatedAt != null
                    || !await ModeratorsHoldAsync(permission, ct))
                    return null;
                var creatorId = item.CreatedByUserId ?? 0;
                var eligible = await uow.Repository<UserAccount>().CountAsync(
                    user => user.Role == UserRole.Moderator && user.Status == AccountStatus.Active && user.Id != creatorId, ct);
                return eligible > 0 ? AdminFallbackMessage : null;

            case AccessConditions.NoActiveModerator:
                if (actor.Role != UserRole.Administrator || !await ModeratorsHoldAsync(permission, ct)) return null;
                var active = await uow.Repository<UserAccount>().CountAsync(
                    user => user.Role == UserRole.Moderator && user.Status == AccountStatus.Active && !user.IsDeleted, ct);
                return active > 0 ? NoActiveModeratorMessage : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Moderator chỉ được tính là "còn người xử lý" khi role Moderator đang giữ đúng quyền này; nếu Administrator đã thu
    /// quyền khỏi Moderator thì Administrator là người xử lý còn lại.
    /// </summary>
    private Task<bool> ModeratorsHoldAsync(string permission, CancellationToken ct) =>
        permissions.RoleHasAsync(RoleCodes.Moderator, permission, ct);
}
