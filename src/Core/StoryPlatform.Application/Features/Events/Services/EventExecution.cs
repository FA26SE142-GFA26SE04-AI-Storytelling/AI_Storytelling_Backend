using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

internal static class EventExecution
{
    // Older versions recorded the Admin's approval on awards and had no publication timestamp.
    public static DateTime? PublishedResultsAt(Event campaign, IEnumerable<EventAward> awards) =>
        campaign.ResultsPublishedAt ??
        (campaign.ConfigVersion == EventConfigVersion.ActivityBased &&
         campaign.ResultsFinalizedAt.HasValue &&
         awards.Any(a => !a.IsDeleted && a.ApprovedByAdminId.HasValue && a.Status != EventAwardStatus.AwardProposed)
            ? campaign.ResultsFinalizedAt
            : null);

    public static bool HasEnded(
        Event campaign,
        IReadOnlyList<EventTrack> tracks,
        IReadOnlyList<EventActivity>? activities = null,
        bool allowPaused = false)
    {
        if (campaign.Status != EventStatus.Published && !(allowPaused && campaign.Status == EventStatus.Paused))
            return false;
        var now = DateTime.UtcNow;
        if (campaign.ConfigVersion == EventConfigVersion.ActivityBased)
        {
            return !((campaign.ClosesAt is { } closesAt && now <= closesAt) ||
                activities == null || activities.Count == 0 || activities.Any(act => now <= act.EndAt ||
                (act.ActivityType == EventActivityType.Submission && act.ReviewCutoffAt is { } cutoff && now <= cutoff)));
        }
        return !(tracks.Count == 0 || tracks.Any(track => now <= track.ExecutionEndAt) ||
            (campaign.VotingEndAt is { } votingEnd && now <= votingEnd) ||
            (EventVoting.CreatorSchedule(campaign) is { } reverse && now <= reverse.VotingEndAt));
    }

    public static void EnsureEnded(
        Event campaign,
        IReadOnlyList<EventTrack> tracks,
        IReadOnlyList<EventActivity>? activities = null,
        bool allowPaused = false)
    {
        if (campaign.Status != EventStatus.Published && !(allowPaused && campaign.Status == EventStatus.Paused))
            throw new ConflictException("Sự kiện chưa ở trạng thái cho phép thực hiện thao tác này.");
        if (!HasEnded(campaign, tracks, activities, allowPaused))
        {
            throw new ConflictException(campaign.ConfigVersion == EventConfigVersion.ActivityBased
                ? "Các hoạt động của sự kiện phải kết thúc trước khi xác minh, chốt giải hoặc đóng sự kiện."
                : "Các nhánh tham gia phải kết thúc trước khi xác minh, chốt giải hoặc đóng sự kiện.");
        }
    }
}
