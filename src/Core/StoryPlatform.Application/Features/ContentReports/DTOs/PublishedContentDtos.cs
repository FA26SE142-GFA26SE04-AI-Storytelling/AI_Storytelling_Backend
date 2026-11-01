using System.Text.Json.Serialization;

namespace StoryPlatform.Application.Features.ContentReports.DTOs;

public enum PublishedContentSource
{
    PublicLibrary = 1,
    RoadmapTemplate = 2
}

public class PublishedContentQueryDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PublishedContentSource Source { get; set; }

    public string? AgeBand { get; set; }
    public int? ContentCategoryId { get; set; }
    public DateTime? PublishedFrom { get; set; }
    public DateTime? PublishedTo { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>Một dòng metadata của danh sách nội dung đã công khai.</summary>
public class PublishedContentItemDto
{
    public string Source { get; set; } = string.Empty;
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string AgeBand { get; set; } = string.Empty;
    public List<ReportedLabelDto> Labels { get; set; } = new();
    public DateTime? PublishedAt { get; set; }
    public int? StoryCount { get; set; }
    public bool NeedsUpdate { get; set; }
}

/// <summary>Nội dung một truyện đã công khai cho nhân sự kiểm duyệt.</summary>
public class PublishedStoryContentDto
{
    public ReportedStoryDto Story { get; set; } = new();
    public ReportedVersionDto? Version { get; set; }
    public List<ReportedSceneDto> Scenes { get; set; } = new();
    public List<ReportedVocabularyDto> Vocabulary { get; set; } = new();
    public List<ReportedQuizDto> Quizzes { get; set; } = new();
}

public class PublishedRoadmapDto
{
    public int RoadmapId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AgeBand { get; set; } = string.Empty;
    public ReportedLabelDto? MainCategory { get; set; }
    public int ReadingLevelMin { get; set; }
    public int ReadingLevelMax { get; set; }
    public int DefaultDaysPerStory { get; set; }
    public DateTime? PublishedAt { get; set; }
    public bool NeedsUpdate { get; set; }
    public List<PublishedRoadmapStoryDto> Stories { get; set; } = new();
}

public class PublishedRoadmapStoryDto
{
    public int StoryId { get; set; }
    public int Position { get; set; }
    public int? Days { get; set; }
    public string? Title { get; set; }
    public string AgeBand { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
}
