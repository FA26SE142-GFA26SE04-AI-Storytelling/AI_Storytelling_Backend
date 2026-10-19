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

/// <summary>
/// Chi tiết một mục report cho Moderator. Cố ý không có danh tính người báo cáo, hồ sơ trẻ hay dữ liệu học tập;
/// mọi văn bản đã che số điện thoại, email và biệt danh bé.
/// </summary>
public class ContentReportDetailDto
{
    public int ItemId { get; set; }
    public int ReportId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AttachmentUrl { get; set; }
    public bool IsReviewItem { get; set; }
    /// <summary>"Claimed" (đang do Moderator giữ) hoặc "Escalated" (đã chuyển Administrator).</summary>
    public string ItemStatus { get; set; } = string.Empty;

    /// <summary>Lý do Moderator chuyển ca lên (đã che thông tin cá nhân); null nếu chưa Escalate.</summary>
    public string? EscalationReason { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public ReportedStoryDto Story { get; set; } = new();
    public ReportedVersionDto? Version { get; set; }
    public List<ReportedSceneDto> Scenes { get; set; } = new();
    public List<ReportedVocabularyDto> Vocabulary { get; set; } = new();
    public List<ReportedQuizDto> Quizzes { get; set; } = new();
    public List<ReportedDiscussionDto> DiscussionQuestions { get; set; } = new();
}

public class ReportedStoryDto
{
    public int StoryId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Genre { get; set; }
    public string AgeBand { get; set; } = string.Empty;
    public int? ReadingLevel { get; set; }
    public string Language { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public List<ReportedLabelDto> Labels { get; set; } = new();
}

public class ReportedLabelDto
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class ReportedVersionDto
{
    public int VersionId { get; set; }
    public int VersionNo { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? Lesson { get; set; }
}

public class ReportedSceneDto
{
    public int SceneIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public List<string> ImageUrls { get; set; } = new();
    public List<string> AudioUrls { get; set; } = new();
}

public class ReportedVocabularyDto
{
    public string Term { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
}

public class ReportedQuizDto
{
    public string Type { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string? CorrectAnswer { get; set; }
    public string? Choices { get; set; }
}

public class ReportedDiscussionDto
{
    public string Question { get; set; } = string.Empty;
    public bool IsMoralLesson { get; set; }
}

public class DismissReportRequestDto
{
    /// <summary>Lý do không vi phạm (bắt buộc, tối đa 1000 ký tự).</summary>
    public string Reason { get; set; } = string.Empty;
}

public class ArchiveReportRequestDto
{
    /// <summary>Lý do gỡ: SafetyConcern, Copyright hoặc Inappropriate.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ArchivedReason ArchiveReason { get; set; }

    /// <summary>Lý do chi tiết nội bộ (bắt buộc, tối đa 1000 ký tự).</summary>
    public string Reason { get; set; } = string.Empty;
}

public class ContentReportDecisionResultDto
{
    public int ItemId { get; set; }
    public int ReportId { get; set; }
    public int StoryId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public bool StoryArchived { get; set; }
    public DateTime DecidedAt { get; set; }
}

public class EscalateReportRequestDto
{
    /// <summary>Lý do chuyển Administrator (bắt buộc, tối đa 1000 ký tự). Chỉ Administrator đọc được.</summary>
    public string Reason { get; set; } = string.Empty;
}

public class CreateReviewItemRequestDto
{
    public int StoryId { get; set; }

    /// <summary>Khi rà soát một truyện trong bộ truyện mẫu: Id bộ (bộ phải đang công khai và chứa truyện).</summary>
    public int? ReadingRoadmapId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ContentReportReason Reason { get; set; }

    /// <summary>Ghi chú tùy chọn, tối đa 2000 ký tự.</summary>
    public string? Description { get; set; }

    /// <summary>Nguồn đính kèm tùy chọn (URL http hoặc https, tối đa 500 ký tự).</summary>
    public string? AttachmentUrl { get; set; }
}
