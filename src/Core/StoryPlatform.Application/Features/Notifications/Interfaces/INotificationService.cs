using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Notifications.Interfaces;

public interface INotificationService
{
    Task<NotificationDto> CreateAsync(
        int recipientUserId, NotificationType type, string? payload, CancellationToken cancellationToken = default);

    Task<List<NotificationDto>> ListAsync(int currentUserId, CancellationToken cancellationToken = default);
    Task MarkAsReadAsync(int notificationId, int currentUserId, CancellationToken cancellationToken = default);
    Task DeleteAsync(int notificationId, int currentUserId, CancellationToken cancellationToken = default);
}
