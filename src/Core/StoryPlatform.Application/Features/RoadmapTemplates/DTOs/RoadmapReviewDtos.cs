namespace StoryPlatform.Application.Features.RoadmapTemplates.DTOs;

public class RoadmapChecklistEntryDto
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string? Note { get; set; }
}

public class SaveRoadmapChecklistRequestDto
{
    public List<RoadmapChecklistEntryDto> Entries { get; set; } = new();
}

public class RoadmapReviewBlockerDto
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? RelatedRoadmapId { get; set; }
}

public class RoadmapReviewInfoDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AgeBand { get; set; } = string.Empty;
    public RoadmapCategoryDto? MainCategory { get; set; }
    public int ReadingLevelMin { get; set; }
    public int ReadingLevelMax { get; set; }
    public int DefaultDaysPerStory { get; set; }
    public int CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public int? ClonedFromId { get; set; }
}

public class RoadmapReviewPackageDto
{
    public int ItemId { get; set; }
    public RoadmapReviewInfoDto Roadmap { get; set; } = new();
    public List<RoadmapStoryDto> Stories { get; set; } = new();
    public List<RoadmapCheckDto> Checks { get; set; } = new();
    public List<RoadmapChecklistEntryDto> Checklist { get; set; } = new();
    public List<RoadmapReviewBlockerDto> Blockers { get; set; } = new();
    public bool CanApprove { get; set; }
    public DateTime ClaimExpiresAt { get; set; }
}

public class RoadmapChangePartDto
{
    public string PartType { get; set; } = string.Empty;
    public int? StoryId { get; set; }
    public string? Action { get; set; }
    public string Note { get; set; } = string.Empty;
}

public class RequestRoadmapChangesDto
{
    public string GeneralNote { get; set; } = string.Empty;
    public List<RoadmapChangePartDto> Parts { get; set; } = new();
}

public class RejectRoadmapRequestDto
{
    public string Reason { get; set; } = string.Empty;
}

public class RoadmapReviewDecisionResultDto
{
    public int ItemId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public int RoadmapId { get; set; }
    public string RoadmapStatus { get; set; } = string.Empty;
    public int? ReplacedRoadmapId { get; set; }
    public DateTime DecidedAt { get; set; }
}
