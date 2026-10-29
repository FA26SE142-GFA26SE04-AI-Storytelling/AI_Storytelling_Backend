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

    /// <summary>Owner của bé và mọi người giám sát còn hiệu lực có quyền ApproveStory, tài khoản đang Active.</summary>
    private async Task<IReadOnlyList<PreparedStoryWishNotification>> SendToApproversAsync(
        int childId, NotificationType type, object payloadObject, bool storeOnly, CancellationToken ct)
    {
        var relationships = await _unitOfWork.Repository<SupervisionRelationship>().FindAsync(
            relationship => relationship.ChildProfileId == childId
                            && relationship.RevokedAt == null
                            && !relationship.IsDeleted,
            cancellationToken: ct);
        var relationshipIds = relationships.Select(relationship => relationship.Id).ToList();
        var approvers = (await _unitOfWork.Repository<SupervisionPermission>().FindAsync(
                permission => relationshipIds.Contains(permission.SupervisionRelationshipId)
                              && permission.Permission == Permission.ApproveStory
                              && !permission.IsDeleted,
                cancellationToken: ct))
            .Select(permission => permission.SupervisionRelationshipId).ToHashSet();
        var recipients = relationships
            .Where(relationship => relationship.SupervisorRole == SupervisorRole.Owner || approvers.Contains(relationship.Id))
            .Select(relationship => relationship.SupervisorUserId)
            .Distinct()
            .ToList();

        var payload = JsonSerializer.Serialize(payloadObject);
        var prepared = new List<PreparedStoryWishNotification>();
        foreach (var recipientId in recipients)
        {
            var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(recipientId, ct);
            if (user == null || user.IsDeleted || user.Status != AccountStatus.Active)
            {
                continue;
            }

            if (storeOnly)
                prepared.Add(new(recipientId, await _notificationService.CreateStoredAsync(recipientId, type, payload, ct)));
            else
                await _notificationService.CreateAsync(recipientId, type, payload, ct);
        }
        return prepared;
    }
}
