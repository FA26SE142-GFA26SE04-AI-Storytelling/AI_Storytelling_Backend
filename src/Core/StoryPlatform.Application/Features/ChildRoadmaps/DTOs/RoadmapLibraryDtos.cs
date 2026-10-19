using System.Text.Json.Serialization;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;

namespace StoryPlatform.Application.Features.ChildRoadmaps;

public class RoadmapLibraryQueryDto
{
    public string? AgeBand { get; set; }
    public int? ReadingLevel { get; set; }
    public int? ContentCategoryId { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class RoadmapLibraryItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AgeBand { get; set; } = string.Empty;
    public RoadmapCategoryDto? MainCategory { get; set; }
    public int ReadingLevelMin { get; set; }
    public int ReadingLevelMax { get; set; }
    public int DefaultDaysPerStory { get; set; }
    public int StoryCount { get; set; }
    public bool NeedsUpdate { get; set; }
    public DateTime? PublishedAt { get; set; }
}

public class RoadmapLibraryStoryDto
{
    public int StoryId { get; set; }
    public int Position { get; set; }
    public int? Days { get; set; }
    public string? Title { get; set; }
    public string AgeBand { get; set; } = string.Empty;
    public int? ReadingLevel { get; set; }
    public bool IsAvailable { get; set; }
}

public class RoadmapLibraryDetailDto : RoadmapLibraryItemDto
{
    public List<RoadmapLibraryStoryDto> Stories { get; set; } = new();
}

public class RoadmapCopyRequestDto
{
    public List<int> ChildProfileIds { get; set; } = new();

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RoadmapInsertPosition Position { get; set; } = RoadmapInsertPosition.End;

    public int? DaysPerStory { get; set; }
}

public class RoadmapStepPlanDto
{
    public int StoryId { get; set; }
    public string? Title { get; set; }
    public int Position { get; set; }
    public int Days { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? SkipCode { get; set; }
    public string? Message { get; set; }
}

public class RoadmapChildPlanDto
{
    public int ChildProfileId { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? SkipCode { get; set; }
    public string? Message { get; set; }
    public List<RoadmapStepPlanDto> Steps { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class RoadmapCopyPlanDto
{
    public int RoadmapId { get; set; }
    public string RoadmapName { get; set; } = string.Empty;
    public List<RoadmapChildPlanDto> Children { get; set; } = new();
}
