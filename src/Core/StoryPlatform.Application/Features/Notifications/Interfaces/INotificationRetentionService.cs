namespace StoryPlatform.Application.Features.Notifications.Interfaces;

public interface INotificationRetentionService
{
    Task<int> PurgeExpiredAsync(
        DateTime utcNow, CancellationToken cancellationToken = default);
}
