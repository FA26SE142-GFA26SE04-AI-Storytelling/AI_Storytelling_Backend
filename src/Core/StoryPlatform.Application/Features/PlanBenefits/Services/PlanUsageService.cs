using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Features.PlanBenefits.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.PlanBenefits.Services;

/// <summary>
/// Truyện thư viện công khai = truyện của Creator (Scope Creator). Mọi hồ sơ trẻ của tài khoản dùng chung
/// một mức (tính theo tài khoản, không theo trẻ). Phiên mở qua gói truyện gia đình không tính.
/// </summary>
public class PlanUsageService(IUnitOfWork unitOfWork) : IPlanUsageService
{
    internal const string EventWeeklyLimitWaiver = "{\"eventWeeklyLimitWaived\":true}";

    public Task<int> CountNewLibraryStoriesThisWeekAsync(
        int ownerUserId, DateTime utcNow, CancellationToken cancellationToken = default) =>
        CountNewLibraryStoriesInPeriodAsync(
            ownerUserId, VietnamTime.StartOfWeekUtc(utcNow), VietnamTime.StartOfNextWeekUtc(utcNow), cancellationToken);

    public async Task<int> CountNewLibraryStoriesInPeriodAsync(
        int ownerUserId, DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken = default) =>
        (await ListNewLibraryStoryIdsInPeriodAsync(ownerUserId, periodStart, periodEnd, cancellationToken)).Count;

    public Task<IReadOnlyList<int>> ListNewLibraryStoryIdsThisWeekAsync(
        int ownerUserId, DateTime utcNow, CancellationToken cancellationToken = default) =>
        ListNewLibraryStoryIdsInPeriodAsync(
            ownerUserId, VietnamTime.StartOfWeekUtc(utcNow), VietnamTime.StartOfNextWeekUtc(utcNow), cancellationToken);

    private async Task<IReadOnlyList<int>> ListNewLibraryStoryIdsInPeriodAsync(
        int ownerUserId, DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken)
    {
        var openedInPeriod = (await FirstOpenedLibraryStoriesAsync(ownerUserId, storyId: null, cancellationToken))
            .Where(session => session.StartedAt >= periodStart && session.StartedAt < periodEnd).ToList();
        if (openedInPeriod.Count == 0)
            return [];

        var sessionIds = openedInPeriod.Select(session => session.Id).ToList();
        var waivers = await unitOfWork.Repository<TelemetryLog>().FindAsync(
            log => sessionIds.Contains(log.ReadingSessionId) && log.EventType == TelemetryEventType.SessionStarted
                && log.EventPayload == EventWeeklyLimitWaiver && !log.IsDeleted,
            cancellationToken: cancellationToken);
        var waivedSessionIds = waivers.Select(log => log.ReadingSessionId).ToHashSet();
        return openedInPeriod.Where(session => !waivedSessionIds.Contains(session.Id)).Select(session => session.StoryId).ToList();
    }

    public async Task<bool> HasOpenedLibraryStoryAsync(int ownerUserId, int storyId, CancellationToken cancellationToken = default) =>
        (await FirstOpenedLibraryStoriesAsync(ownerUserId, storyId, cancellationToken)).Count > 0;

    public async Task<int> CountWritingAssistsTodayAsync(int userId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var dayStart = VietnamTime.StartOfDayUtc(utcNow);
        return await unitOfWork.Repository<StoryAssistanceTask>().CountAsync(
            task => task.OwnerUserId == userId && task.CreatedAt >= dayStart, cancellationToken);
    }

    /// <summary>Lần mở đầu tiên của mỗi truyện thư viện trên mọi hồ sơ trẻ của tài khoản.</summary>
    private async Task<IReadOnlyList<ReadingSession>> FirstOpenedLibraryStoriesAsync(
        int ownerUserId, int? storyId, CancellationToken cancellationToken)
    {
        var childIds = (await unitOfWork.Repository<ChildProfile>().FindAsync(
                child => child.OwnerUserId == ownerUserId, cancellationToken: cancellationToken))
            .Select(child => child.Id)
            .ToList();
        if (childIds.Count == 0)
        {
            return [];
        }

        var sessions = await unitOfWork.Repository<ReadingSession>().FindAsync(
            session => childIds.Contains(session.ChildProfileId) && session.FamilyConfirmationId == null
                       && (storyId == null || session.StoryId == storyId),
            cancellationToken: cancellationToken);
        if (sessions.Count == 0)
        {
            return [];
        }

        var storyIds = sessions.Select(session => session.StoryId).Distinct().ToList();
        var libraryStoryIds = (await unitOfWork.Repository<Story>().FindAsync(
                story => storyIds.Contains(story.Id) && story.Scope == StoryScope.Creator,
                cancellationToken: cancellationToken))
            .Select(story => story.Id)
            .ToHashSet();

        return sessions
            .Where(session => libraryStoryIds.Contains(session.StoryId))
            .GroupBy(session => session.StoryId)
            .Select(group => group.OrderBy(session => session.StartedAt).ThenBy(session => session.Id).First())
            .ToList();
    }
}
