namespace StoryPlatform.Application.Features.BusinessReports.DTOs;

public class GenerateBusinessReportRequestDto
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
}

public class BusinessReportDto
{
    public int Id { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int StoriesGenerated { get; set; }
    public int StoriesApproved { get; set; }
    public int StoriesRejected { get; set; }
    public int ReadingSessionsCompleted { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
}
