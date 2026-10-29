using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.StoryWishes.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryWishes.Services;

public class StoryWishNotifier : IStoryWishNotifier
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;

    public StoryWishNotifier(IUnitOfWork unitOfWork, INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
    }

    public Task NotifySubmittedAsync(StoryWish wish, CancellationToken ct = default)
    {
        return SendToApproversAsync(wish.ChildProfileId, NotificationType.StoryWishSubmitted, new
        {
            childProfileId = wish.ChildProfileId,
            wishId = wish.Id,
            kind = wish.Kind.ToString()
        }, ct);
    }

    public Task NotifyExpiringAsync(StoryWish wish, CancellationToken ct = default)
    {
        return SendToApproversAsync(wish.ChildProfileId, NotificationType.StoryWishExpiring, new
        {
            childProfileId = wish.ChildProfileId,
            wishId = wish.Id,
            kind = wish.Kind.ToString(),
            expiresAt = (wish.PendingSince ?? wish.CreatedAt) + StoryWishDecisionPolicy.PendingLifetime
        }, ct);
    }

    /// <summary>Chủ hồ sơ của bé, tài khoản đang Active.</summary>
    private async Task SendToApproversAsync(int childId, NotificationType type, object payloadObject, CancellationToken ct)
    {
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childId, ct);
        if (child == null || child.IsDeleted || child.Status == ChildProfileStatus.Deleted) return;
        var owner = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(child.OwnerUserId, ct);
        if (owner == null || owner.IsDeleted || owner.Status != AccountStatus.Active) return;
        await _notificationService.CreateAsync(child.OwnerUserId, type, JsonSerializer.Serialize(payloadObject), ct);
    }
}
