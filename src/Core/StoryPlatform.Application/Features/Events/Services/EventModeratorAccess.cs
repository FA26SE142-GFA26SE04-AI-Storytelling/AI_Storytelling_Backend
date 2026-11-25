using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

internal static class EventModeratorAccess
{
    public static int OwnerId(Event campaign) =>
        campaign.AssignedModeratorId > 0 ? campaign.AssignedModeratorId : campaign.CreatedByUserId;

    public static void RequireOwner(Event campaign, int actorUserId)
    {
        if (actorUserId <= 0 || OwnerId(campaign) != actorUserId)
            throw new ForbiddenException("Chỉ Moderator phụ trách chính được thực hiện thao tác này.");
    }

    public static Task<bool> IsAssistantAsync(IUnitOfWork uow, int eventId, int actorUserId, CancellationToken ct) =>
        uow.Repository<EventModeratorAssignment>().ExistsAsync(
            assignment => assignment.EventId == eventId && assignment.ModeratorId == actorUserId, ct);

    public static async Task RequireOperatorAsync(IUnitOfWork uow, Event campaign, int actorUserId, CancellationToken ct)
    {
        if (actorUserId > 0 && OwnerId(campaign) == actorUserId) return;
        var actor = await uow.Repository<UserAccount>().GetByIdAsync(actorUserId, ct);
        if (actor is { Status: AccountStatus.Active, IsDeleted: false } &&
            (actor.Role == UserRole.Administrator ||
             (actor.Role == UserRole.Moderator && await IsAssistantAsync(uow, campaign.Id, actorUserId, ct)))) return;
        throw new ForbiddenException("Bạn chưa được phân công điều phối sự kiện này.");
    }
}
