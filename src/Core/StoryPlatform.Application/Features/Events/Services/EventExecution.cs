using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

internal static class EventExecution
{
    public static void EnsureEnded(
        Event campaign,
        IReadOnlyList<EventTrack> tracks,
        IReadOnlyList<EventActivity>? activities = null,
        bool allowPaused = false)
    {
        if (campaign.Status != EventStatus.Published && !(allowPaused && campaign.Status == EventStatus.Paused))
            throw new ConflictException("Sự kiện chưa ở trạng thái cho phép thực hiện thao tác này.");
        var now = DateTime.UtcNow;
        if (campaign.ConfigVersion == EventConfigVersion.ActivityBased)
        {
            if ((campaign.ClosesAt is { } closesAt && now <= closesAt) ||
                activities == null || activities.Count == 0 || activities.Any(act => now <= act.EndAt ||
                (act.ActivityType == EventActivityType.Submission && act.ReviewCutoffAt is { } cutoff && now <= cutoff)))
                throw new ConflictException("Các hoạt động của sự kiện phải kết thúc trước khi xác minh, chốt giải hoặc đóng sự kiện.");
            return;
        }
        if (tracks.Count == 0 || tracks.Any(track => now <= track.ExecutionEndAt) ||
            (campaign.VotingEndAt is { } votingEnd && now <= votingEnd) ||
            (EventVoting.CreatorSchedule(campaign) is { } reverse && now <= reverse.VotingEndAt))
            throw new ConflictException("Các nhánh tham gia phải kết thúc trước khi xác minh, chốt giải hoặc đóng sự kiện.");
    }
}
