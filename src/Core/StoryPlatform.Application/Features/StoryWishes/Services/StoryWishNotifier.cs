using System.Text.Json;
using Microsoft.Extensions.Logging;
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
    private readonly INotificationRealtimePublisher _publisher;
    private readonly ILogger<StoryWishNotifier> _logger;

    public StoryWishNotifier(IUnitOfWork unitOfWork, INotificationService notificationService,
        INotificationRealtimePublisher publisher, ILogger<StoryWishNotifier> logger)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
        _publisher = publisher;
        _logger = logger;
    }

    public Task NotifySubmittedAsync(StoryWish wish, CancellationToken ct = default)
    {
        return SendToApproversAsync(wish.ChildProfileId, NotificationType.StoryWishSubmitted, new
        {
            childProfileId = wish.ChildProfileId,
            wishId = wish.Id,
            kind = wish.Kind.ToString()
        }, false, ct);
    }

    public Task<IReadOnlyList<PreparedStoryWishNotification>> PrepareExpiringAsync(StoryWish wish, CancellationToken ct = default)
    {
        return SendToApproversAsync(wish.ChildProfileId, NotificationType.StoryWishExpiring, new
        {
            childProfileId = wish.ChildProfileId,
            wishId = wish.Id,
            kind = wish.Kind.ToString(),
            expiresAt = (wish.PendingSince ?? wish.CreatedAt) + StoryWishDecisionPolicy.PendingLifetime
        }, true, ct);
    }

    public async Task PublishPreparedAsync(IReadOnlyList<PreparedStoryWishNotification> notifications, CancellationToken ct = default)
    {
        if (notifications.Count == 0 || ct.IsCancellationRequested) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        foreach (var notification in notifications)
        {
            try
            {
                await _publisher.PublishAsync(notification.RecipientUserId, notification.Notification, timeout.Token)
                    .WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                _logger.LogWarning("Committed wish notification delivery interrupted. NotificationId={NotificationId}, RecipientId={RecipientId}",
                    notification.Notification.Id, notification.RecipientUserId);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Committed wish notification realtime delivery failed. NotificationId={NotificationId}, RecipientId={RecipientId}, ErrorType={ErrorType}",
                    notification.Notification.Id, notification.RecipientUserId, ex.GetType().Name);
            }
        }
    }

    /// <summary>Chủ hồ sơ của bé, tài khoản đang Active. Lưu reminder trong transaction, phát sau commit.</summary>
    private async Task<IReadOnlyList<PreparedStoryWishNotification>> SendToApproversAsync(
        int childId, NotificationType type, object payloadObject, bool storeOnly, CancellationToken ct)
    {
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childId, ct);
        if (child == null || child.IsDeleted || child.Status == ChildProfileStatus.Deleted)
            return Array.Empty<PreparedStoryWishNotification>();
        var owner = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(child.OwnerUserId, ct);
        if (owner == null || owner.IsDeleted || owner.Status != AccountStatus.Active)
            return Array.Empty<PreparedStoryWishNotification>();
        var payload = JsonSerializer.Serialize(payloadObject);
        if (storeOnly)
            return new[] { new PreparedStoryWishNotification(child.OwnerUserId,
                await _notificationService.CreateStoredAsync(child.OwnerUserId, type, payload, ct)) };
        await _notificationService.CreateAsync(child.OwnerUserId, type, payload, ct);
        return Array.Empty<PreparedStoryWishNotification>();
    }
}
