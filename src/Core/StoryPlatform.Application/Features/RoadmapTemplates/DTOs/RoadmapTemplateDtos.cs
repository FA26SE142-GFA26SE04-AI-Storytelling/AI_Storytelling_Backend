using System.Text.Json.Serialization;

namespace StoryPlatform.Application.Features.RoadmapTemplates.DTOs;

public class SaveRoadmapRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AgeBand { get; set; } = string.Empty;
    public int MainCategoryId { get; set; }
    public int ReadingLevelMin { get; set; }
    public int ReadingLevelMax { get; set; }
    public int DefaultDaysPerStory { get; set; } = 1;
}

public class RoadmapCategoryDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class RoadmapStoryDto
{
    public int StoryId { get; set; }
    public int Position { get; set; }
    public int? Days { get; set; }
    public string? Title { get; set; }
    public string AgeBand { get; set; } = string.Empty;
    public int? ReadingLevel { get; set; }
    public bool IsEligible { get; set; }
}

public class RoadmapCheckDto
{
    public string Code { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? RelatedRoadmapId { get; set; }
}

public class RoadmapDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AgeBand { get; set; } = string.Empty;
    public RoadmapCategoryDto? MainCategory { get; set; }
    public int ReadingLevelMin { get; set; }
    public int ReadingLevelMax { get; set; }
    public int DefaultDaysPerStory { get; set; }
    public string Status { get; set; } = string.Empty;
    public int CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
    public bool IsMine { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int? ClonedFromId { get; set; }
    public List<RoadmapStoryDto> Stories { get; set; } = new();
    public List<RoadmapCheckDto> Checks { get; set; } = new();
    public bool CanSubmit { get; set; }
}

public class CandidateQueryDto
{
    public string? AgeBand { get; set; }
    public int? ReadingLevelMin { get; set; }
    public int? ReadingLevelMax { get; set; }
    public int? ContentCategoryId { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class CandidateStoryDto
{
    public int StoryId { get; set; }
    public string? Title { get; set; }
    public string AgeBand { get; set; } = string.Empty;
    public int? ReadingLevel { get; set; }
    public List<RoadmapCategoryDto> Labels { get; set; } = new();
    public DateTime? PublishedAt { get; set; }
}

public class RoadmapItemInputDto
{
    public int StoryId { get; set; }
    public int? Days { get; set; }
}

public class SetRoadmapItemsRequestDto
{
    public List<RoadmapItemInputDto> Items { get; set; } = new();
}

public class RoadmapSubmitResultDto
{
    public int RoadmapId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? ItemId { get; set; }
}

public enum RoadmapListGroup
{
    Drafts = 1,
    Pending = 2,
    Published = 3,
    Archived = 4
}

public class RoadmapListQueryDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RoadmapListGroup Group { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class RoadmapListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AgeBand { get; set; } = string.Empty;
    public RoadmapCategoryDto? MainCategory { get; set; }
    public int StoryCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public int CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
    public bool IsMine { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}
