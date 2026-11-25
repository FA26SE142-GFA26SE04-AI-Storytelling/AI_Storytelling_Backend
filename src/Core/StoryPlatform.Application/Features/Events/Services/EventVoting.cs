using System;
using System.Text.Json;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

internal static class EventVoting
{
    public static bool IsEnabled(EventScoringMode mode) =>
        AllowsVoter(mode, EventTrackType.Parent) || AllowsVoter(mode, EventTrackType.Creator);

    public static bool AllowsVoter(EventScoringMode mode, EventTrackType voter) =>
        mode == EventScoringMode.CrossRoleVoteAverage ||
        (mode == EventScoringMode.ParentVoteAverage && voter == EventTrackType.Parent) ||
        (mode == EventScoringMode.CreatorVoteAverage && voter == EventTrackType.Creator);

    public static bool RanksAuthors(EventScoringMode mode, EventTrackType author) =>
        AllowsVoter(mode, author == EventTrackType.Parent ? EventTrackType.Creator : EventTrackType.Parent);

    public static EventVotingScheduleDto? CreatorSchedule(Event campaign) =>
        campaign.CreatorVotingScheduleJson is { Length: > 0 } json
            ? JsonSerializer.Deserialize<EventVotingScheduleDto>(json)
            : null;
}
