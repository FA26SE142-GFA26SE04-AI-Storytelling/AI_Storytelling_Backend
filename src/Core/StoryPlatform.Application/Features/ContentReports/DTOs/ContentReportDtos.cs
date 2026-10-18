using System.Text.Json.Serialization;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentReports.DTOs;

public class CreateContentReportRequestDto
{
    public int StoryId { get; set; }

    /// <summary>SafetyConcern, Inappropriate, WrongAgeBand, Copyright, Cultural hoặc Other.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ContentReportReason Reason { get; set; }

    /// <summary>Mô tả tùy chọn, tối đa 2000 ký tự.</summary>
    public string? Description { get; set; }

    /// <summary>Nguồn đính kèm tùy chọn, URL http/https tuyệt đối tối đa 500 ký tự.</summary>
    public string? AttachmentUrl { get; set; }
}

public class ContentReportResultDto
{
    public int ReportId { get; set; }
    public int StoryId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
