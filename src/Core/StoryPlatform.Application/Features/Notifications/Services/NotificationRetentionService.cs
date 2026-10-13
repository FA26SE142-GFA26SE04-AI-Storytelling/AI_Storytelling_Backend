using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.Notifications.Services;

public class NotificationRetentionService : INotificationRetentionService
{
    private readonly IUnitOfWork _unitOfWork;

    public NotificationRetentionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<int> PurgeExpiredAsync(
        DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var cutoff = utcNow.AddDays(-NotificationRetention.Days);
        var repository = _unitOfWork.Repository<Notification>();
        var expired = await repository.FindAsync(
            notification => notification.CreatedAt < cutoff,
            cancellationToken: cancellationToken);
        if (expired.Count == 0)
        {
            return 0;
        }

        repository.DeleteRange(expired);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }
}
