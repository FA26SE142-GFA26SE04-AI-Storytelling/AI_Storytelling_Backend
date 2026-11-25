using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

public partial class EventManagementService
{
    public async Task EnsureStaffEventAccessAsync(int eventId, int actorUserId, CancellationToken ct = default)
    {
        var campaign = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        await EventModeratorAccess.RequireOperatorAsync(_uow, campaign, actorUserId, ct);
    }

    public Task<EventDetailDto> UpdateLeadModeratorAsync(int adminUserId, int eventId, UpdateEventLeadModeratorRequestDto request, CancellationToken ct = default) =>
        InTransactionAsync(eventId, async () =>
        {
            var campaign = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
                ?? throw new NotFoundException(nameof(Event), eventId);
            if (campaign.Status is EventStatus.Closed or EventStatus.Archived)
                throw new ConflictException("Sự kiện đã kết thúc nên không thể đổi Moderator phụ trách.");

            var moderatorId = request.ModeratorId;
            var previousModeratorId = EventModeratorAccess.OwnerId(campaign);
            var repository = _uow.Repository<EventModeratorAssignment>();
            var previousAssignments = await repository.FindAsync(assignment => assignment.EventId == eventId, cancellationToken: ct);
            var previousAssistantIds = previousAssignments.Select(assignment => assignment.ModeratorId).ToHashSet();
            var assistants = request.AssistantModeratorIds?.ToHashSet() ?? previousAssistantIds.Where(id => id != moderatorId).ToHashSet();
            if (assistants.Contains(moderatorId))
                throw new BadRequestException("Moderator phụ trách chính không thể đồng thời là người hỗ trợ.");
            var requestedIds = assistants.Append(moderatorId).Distinct().ToList();
            var activeModerators = await _uow.Repository<UserAccount>().FindAsync(
                user => requestedIds.Contains(user.Id) && user.Role == UserRole.Moderator &&
                    user.Status == AccountStatus.Active && !user.IsDeleted, cancellationToken: ct);
            if (activeModerators.Count != requestedIds.Count)
                throw new BadRequestException("Chỉ có thể phân công các Moderator đang hoạt động.");

            if (previousModeratorId != moderatorId || !previousAssistantIds.SetEquals(assistants))
            {
                campaign.AssignedModeratorId = moderatorId;
                campaign.UpdatedAt = DateTime.UtcNow;
                _uow.Repository<Event>().Update(campaign);
                repository.DeleteRange(previousAssignments.Where(assignment => !assistants.Contains(assignment.ModeratorId)));
                foreach (var assistantId in assistants.Except(previousAssistantIds))
                    await repository.AddAsync(new EventModeratorAssignment { EventId = eventId, ModeratorId = assistantId }, ct);
                await _uow.SaveChangesAsync(ct);
                await EventSubmissionModerationAccess.ReleaseUnauthorizedClaimedItemsForEventAsync(
                    _uow, _audit, adminUserId, eventId, DateTime.UtcNow, ct);
                await _audit.LogAsync(adminUserId, "EventModeratorsAssigned", nameof(Event), eventId, null,
                    new { previousModeratorId, moderatorId, assistantModeratorIds = assistants.OrderBy(id => id).ToArray() }, ct);
            }
            return await BuildDetailAsync(campaign, ct, adminUserId, UserRole.Administrator);
        }, ct);
}
