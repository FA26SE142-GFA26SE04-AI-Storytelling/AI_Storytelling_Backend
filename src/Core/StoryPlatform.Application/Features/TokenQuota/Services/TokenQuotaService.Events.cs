using System.Text.Json;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.TokenQuota.Services;

public partial class TokenQuotaService
{
    public async Task EnsureCreatorEventWithinQuotaAsync(int creatorUserId, int eventId, CancellationToken cancellationToken = default)
    {
        await ValidateEventSubmissionAccessAsync(creatorUserId, UserRole.ContentCreator, eventId, cancellationToken);
        await EnsureCreatorMonthlyGrantedAsync(creatorUserId, DateTime.UtcNow, cancellationToken);
        var lots = await GetStoryLotsAsync(creatorUserId, null, CreditType.AI, eventId, cancellationToken);
        if (lots.Sum(x => x.Remaining) <= 0) throw new ConflictException(CreatorOutOfAiCreditsMessage);
    }

    public async Task IncrementCreatorEventUsageAsync(int creatorUserId, Story story, int eventId, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.AcquireTransactionLockAsync(creatorUserId, cancellationToken);
        await ValidateEventSubmissionAccessAsync(creatorUserId, UserRole.ContentCreator, eventId, cancellationToken);
        await EnsureCreatorMonthlyGrantedAsync(creatorUserId, DateTime.UtcNow, cancellationToken);
        await HoldOneCreditAsync(creatorUserId, story, CreatorOutOfAiCreditsMessage, cancellationToken, eventId);
    }

    public async Task EnsureParentEventWithinQuotaAsync(int userId, int childProfileId, int eventId, CancellationToken cancellationToken = default)
    {
        var child = await GetChildAsync(childProfileId, cancellationToken);
        if (child.OwnerUserId != userId || child.IsDeleted || child.Status != ChildProfileStatus.Active)
            throw new ForbiddenException("Hồ sơ trẻ không thuộc quyền quản lý của bạn.");
        await ValidateEventSubmissionAccessAsync(userId, UserRole.User, eventId, cancellationToken);
        await EnsureTrialGrantedAsync(userId, cancellationToken);
        var lots = await GetStoryLotsAsync(userId, null, CreditType.AI, eventId, cancellationToken);
        if (lots.Sum(x => x.Remaining) <= 0) throw new ConflictException(OutOfCreditsMessage);
    }

    public async Task IncrementParentEventUsageAsync(int userId, int childProfileId, Story story, int eventId, CancellationToken cancellationToken = default)
    {
        var child = await GetChildAsync(childProfileId, cancellationToken);
        if (child.OwnerUserId != userId || child.IsDeleted || child.Status != ChildProfileStatus.Active)
            throw new ForbiddenException("Hồ sơ trẻ không thuộc quyền quản lý của bạn.");
        await _unitOfWork.AcquireTransactionLockAsync(userId, cancellationToken);
        await ValidateEventSubmissionAccessAsync(userId, UserRole.User, eventId, cancellationToken);
        await EnsureTrialGrantedAsync(userId, cancellationToken);
        await HoldOneCreditAsync(userId, story, OutOfCreditsMessage, cancellationToken, eventId);
    }

    private async Task ValidateEventSubmissionAccessAsync(int userId, UserRole expectedRole, int eventId, CancellationToken ct)
    {
        var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, ct);
        if (user == null || user.Role != expectedRole || user.Status != AccountStatus.Active || user.IsDeleted)
            throw new ForbiddenException();

        var ev = await _unitOfWork.Repository<Event>().GetByIdAsync(eventId, ct);
        var now = DateTime.UtcNow;
        if (ev is not { Status: EventStatus.Published, IsDeleted: false })
            throw new ConflictException("Sự kiện chưa mở hoặc đã hết thời gian sáng tác.");

        if (ev.ConfigVersion == EventConfigVersion.ActivityBased)
        {
            var act = await _unitOfWork.Repository<EventActivity>().FirstOrDefaultAsync(
                x => x.EventId == eventId && x.ActivityType == EventActivityType.Submission, cancellationToken: ct);
            if (act == null || now < act.StartAt || now > act.EndAt)
                throw new ConflictException("Sự kiện chưa mở hoặc đã hết thời gian sáng tác.");
            var eligible = act.AllowedAudience == EventAudience.Both ||
                           (act.AllowedAudience == EventAudience.Creator && expectedRole == UserRole.ContentCreator) ||
                           (act.AllowedAudience == EventAudience.Parent && expectedRole == UserRole.User);
            if (!eligible)
                throw new ForbiddenException("Vai trò của bạn không thuộc đối tượng hoạt động sáng tác.");
            if (!await _unitOfWork.Repository<EventParticipation>().ExistsAsync(
                x => x.EventId == eventId && x.EventActivityId == act.Id && x.UserId == userId &&
                     x.Status != EventParticipationStatus.Disqualified, ct))
                throw new ForbiddenException("Bạn chưa đăng ký hoạt động sáng tác của sự kiện.");
            return;
        }

        if (expectedRole != UserRole.ContentCreator)
            throw new ForbiddenException();
        var track = await _unitOfWork.Repository<EventTrack>().FirstOrDefaultAsync(
            x => x.EventId == eventId && x.TrackType == EventTrackType.Creator, cancellationToken: ct);
        if (track == null || now < track.ExecutionStartAt || now > track.ExecutionEndAt)
            throw new ConflictException("Sự kiện chưa mở hoặc đã hết thời gian sáng tác.");
        if (!await _unitOfWork.Repository<EventParticipation>().ExistsAsync(
            x => x.EventId == eventId && x.TrackId == track.Id && x.UserId == userId &&
                 x.Status != EventParticipationStatus.Disqualified, ct))
            throw new ForbiddenException("Bạn chưa đăng ký nhánh sáng tác của sự kiện.");
    }

    private async Task<int?> StoryEventAsync(Story story, CancellationToken ct)
    {
        var submission = await _unitOfWork.Repository<EventSubmission>().FirstOrDefaultAsync(
            x => x.StoryId == story.Id && x.CreatorUserId == story.AuthorUserId &&
                 x.Status != EventSubmissionStatus.Disqualified, cancellationToken: ct);
        if (submission != null) return submission.EventId;
        return await StoryGenerationEventAsync(story.Id, ct);
    }

    private async Task<int?> StoryGenerationEventAsync(int storyId, CancellationToken ct, int? requestId = null)
    {
        var repo = _unitOfWork.Repository<StoryGenerationRequest>();
        var request = requestId is int id
            ? await repo.FirstOrDefaultAsync(x => x.Id == id && x.StoryId == storyId, cancellationToken: ct)
            : (await repo.GetPagedAsync(1, 1, x => x.StoryId == storyId,
                orderBy: items => items.OrderByDescending(x => x.Id), cancellationToken: ct)).Items.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(request?.ContextSnapshotJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<AIStoryInputContextSnapshot>(request.ContextSnapshotJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))?.EventId;
        }
        catch (JsonException) { return null; }
    }

    private async Task<IReadOnlyList<CreditLot>> GetStoryLotsAsync(
        int userId, Story? story, CreditType type, int? eventId, CancellationToken ct,
        IReadOnlyList<CreditLot>? availableLots = null, bool fallbackWhenEventClosed = false)
    {
        var now = DateTime.UtcNow;
        var explicitEvent = eventId.HasValue;
        var lots = availableLots ?? await GetUsableLotsAsync(userId, now, ct, type,
            includeEventGrants: explicitEvent || story is { Scope: StoryScope.Creator } && story.AuthorUserId == userId);
        var ordinary = lots.Where(lot => lot.Source != CreditSource.EventGrant).ToArray();
        var expectedRole = UserRole.ContentCreator;
        if (story != null)
        {
            if (story.AuthorUserId != userId) return ordinary;
            if (story.Scope == StoryScope.Parent)
            {
                if (!eventId.HasValue) return ordinary;
                expectedRole = UserRole.User;
            }
            else if (story.Scope != StoryScope.Creator)
                return ordinary;
            if (!explicitEvent && !lots.Any(lot => lot.Source == CreditSource.EventGrant))
                return ordinary;
            eventId ??= await StoryEventAsync(story, ct);
        }
        else
        {
            var account = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, ct);
            if (account?.Role == UserRole.User) expectedRole = UserRole.User;
        }
        if (!eventId.HasValue) return ordinary;
        try { await ValidateEventSubmissionAccessAsync(userId, expectedRole, eventId.Value, ct); }
        catch (ConflictException) when (!explicitEvent || fallbackWhenEventClosed) { return ordinary; }
        catch (ForbiddenException) when (!explicitEvent || fallbackWhenEventClosed) { return ordinary; }
        return lots.Where(lot => lot.Source != CreditSource.EventGrant || lot.EventId == eventId).ToArray();
    }
}
