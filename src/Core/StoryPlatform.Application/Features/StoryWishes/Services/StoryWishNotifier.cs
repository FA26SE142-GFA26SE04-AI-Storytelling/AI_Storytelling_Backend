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

    /// <summary>Owner của bé và mọi người giám sát còn hiệu lực có quyền ApproveStory, tài khoản đang Active.</summary>
    private async Task SendToApproversAsync(int childId, NotificationType type, object payloadObject, CancellationToken ct)
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
        foreach (var recipientId in recipients)
        {
            var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(recipientId, ct);
            if (user == null || user.IsDeleted || user.Status != AccountStatus.Active)
            {
                continue;
            }

            await _notificationService.CreateAsync(recipientId, type, payload, ct);
        }
    }
}
