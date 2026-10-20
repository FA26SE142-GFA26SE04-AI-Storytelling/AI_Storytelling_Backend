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
        var relationships = await ActiveRelationshipsAsync(childProfileId, cancellationToken);
        var recipients = relationships.Select(r => r.SupervisorUserId).Where(id => id != actorUserId).Distinct();
        await SendAsync(recipients, type, payload, cancellationToken);
    }

    public async Task NotifyRoadmapCompletedAsync(int childProfileId, CancellationToken cancellationToken = default)
    {
        var relationships = await ActiveRelationshipsAsync(childProfileId, cancellationToken);
        var relationshipIds = relationships.Select(r => r.Id).ToList();
        var viewers = await _unitOfWork.Repository<SupervisionPermission>().FindAsync(
            permission => relationshipIds.Contains(permission.SupervisionRelationshipId)
                          && permission.Permission == Permission.ViewProgress,
            cancellationToken: cancellationToken);
        var viewerRelationshipIds = viewers.Select(permission => permission.SupervisionRelationshipId).ToHashSet();
        var recipients = relationships
            .Where(r => r.SupervisorRole == SupervisorRole.Owner || viewerRelationshipIds.Contains(r.Id))
            .Select(r => r.SupervisorUserId).Distinct();

        await SendAsync(recipients, NotificationType.RoadmapCompleted, new { childProfileId }, cancellationToken);
    }

    private async Task<IReadOnlyList<SupervisionRelationship>> ActiveRelationshipsAsync(
        int childProfileId, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<SupervisionRelationship>().FindAsync(
            relationship => relationship.ChildProfileId == childProfileId && relationship.RevokedAt == null,
            cancellationToken: cancellationToken);

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
