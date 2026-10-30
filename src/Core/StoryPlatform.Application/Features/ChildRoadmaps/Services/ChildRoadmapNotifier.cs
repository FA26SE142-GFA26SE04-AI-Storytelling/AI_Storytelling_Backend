using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

public class ChildRoadmapNotifier : IChildRoadmapNotifier
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;

    public ChildRoadmapNotifier(IUnitOfWork unitOfWork, INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
    }

    public async Task NotifyOtherSupervisorsAsync(
        int childProfileId, int actorUserId, NotificationType type, object payload,
        CancellationToken cancellationToken = default)
    {
        var ownerId = await OwnerIdAsync(childProfileId, cancellationToken);
        var recipients = ownerId is int id && id != actorUserId ? new[] { id } : Array.Empty<int>();
        await SendAsync(recipients, type, payload, cancellationToken);
    }

    public async Task NotifyRoadmapCompletedAsync(int childProfileId, CancellationToken cancellationToken = default)
    {
        var ownerId = await OwnerIdAsync(childProfileId, cancellationToken);
        if (ownerId is int id)
            await SendAsync(new[] { id }, NotificationType.RoadmapCompleted, new { childProfileId }, cancellationToken);
    }

    private async Task<int?> OwnerIdAsync(
        int childProfileId, CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken))?.OwnerUserId;

    private async Task SendAsync(
        IEnumerable<int> recipientIds, NotificationType type, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        foreach (var recipientId in recipientIds.ToList())
        {
            var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(recipientId, cancellationToken);
            if (user == null || user.IsDeleted || user.Status != AccountStatus.Active)
            {
                continue;
            }

            await _notificationService.CreateAsync(recipientId, type, json, cancellationToken);
        }
    }
}
