using StoryPlatform.Application.Features.Notifications.DTOs;

namespace StoryPlatform.Application.Features.Notifications.Interfaces;

/// <summary>
/// Kênh đẩy thông báo theo thời gian thực, được tầng API cài đặt bằng SignalR.
/// </summary>
public interface INotificationRealtimePublisher
{
    Task PublishAsync(int recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default);
}
