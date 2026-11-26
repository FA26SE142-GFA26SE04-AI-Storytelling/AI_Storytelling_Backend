using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;

namespace StoryPlatform.Application.Features.Events.Services;

internal static class EventNotificationDelivery
{
    public static async Task PublishAsync(
        INotificationRealtimePublisher? publisher,
        IEnumerable<(int UserId, NotificationDto Notice)> notices,
        ILogger? logger)
    {
        if (publisher is null) return;
        foreach (var (userId, notice) in notices)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await publisher.PublishAsync(userId, notice, timeout.Token);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Could not publish stored event notification {NotificationId}", notice.Id);
            }
        }
    }
}
