using System.Text.Json.Serialization;

namespace StoryPlatform.Application.Features.ChildRoadmaps;

public enum RoadmapInsertPosition
{
    NextUp = 1,
    End = 2
}

public static class ChildRoadmapPolicy
{
    public const string WishNote = "Từ điều ước của em";
    public const int MaxNoteLength = 500;
    public const int MinExpectedDays = 1;
    public const int MaxExpectedDays = 90;
    public const int MaxChildrenPerRequest = 20;
    public const int HistoryLimit = 20;
}

public static class ChildRoadmapSkipCodes
{
    public const string NoPermission = "no_permission";
    public const string ProfileInactive = "profile_inactive";
    public const string NoAccessToStory = "no_access_to_story";
    public const string BlockedTopic = "blocked_topic";
    public const string WrongAge = "wrong_age";
    public const string TooLong = "too_long";
    public const string StoryUnavailable = "story_unavailable";
    public const string AlreadyInRoadmap = "already_in_roadmap";
}

public class AddRoadmapStoryRequestDto
{
    public int StoryId { get; set; }
    public List<int> ChildProfileIds { get; set; } = new();

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RoadmapInsertPosition Position { get; set; } = RoadmapInsertPosition.End;

    public int? ExpectedDays { get; set; }
    public string? Note { get; set; }
}

public class ChildAddResultDto
{
    public int ChildProfileId { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? SkipCode { get; set; }
    public string? Message { get; set; }
    public int? ItemId { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class AddRoadmapStoryResultDto
{
    public List<ChildAddResultDto> Results { get; set; } = new();
}

public class WishRoadmapResultDto
{
    public int ItemId { get; set; }
    public string Outcome { get; set; } = string.Empty;
}

public class ReorderRoadmapRequestDto
{
    public List<int> ItemIds { get; set; } = new();
}

public class ChildRoadmapItemDto
{
    public int Id { get; set; }
    public int StoryId { get; set; }
    public string? Title { get; set; }
    public int Position { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? SkipReason { get; set; }
    public string? Note { get; set; }
    public int? ExpectedDays { get; set; }
    public DateTime? ExpectedCompletionAt { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int AddedByUserId { get; set; }
    public bool IsStoryAvailable { get; set; }
}

public class ChildRoadmapDto
{
    public int ChildProfileId { get; set; }
    public ChildRoadmapItemDto? Open { get; set; }
    public List<ChildRoadmapItemDto> Waiting { get; set; } = new();
    public List<ChildRoadmapItemDto> History { get; set; } = new();
    public int CompletedCount { get; set; }
    public int TotalCount { get; set; }
}
