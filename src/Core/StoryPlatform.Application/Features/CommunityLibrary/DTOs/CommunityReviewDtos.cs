using StoryPlatform.Application.Features.ContentReports.DTOs;

namespace StoryPlatform.Application.Features.CommunityLibrary;

public class CommunityChecklistEntryDto
{
    /// <summary>safety_baseline, no_personal_info hoặc metadata_labels.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Tên tiêu chí (server điền; request không cần gửi).</summary>
    public string Label { get; set; } = string.Empty;
    public bool Passed { get; set; }

    /// <summary>Ghi chú tùy chọn, tối đa 500 ký tự.</summary>
    public string? Note { get; set; }
}

public class SaveCommunityChecklistRequestDto
{
    /// <summary>Các tiêu chí cần cập nhật (có thể chỉ một phần); tiêu chí không gửi giữ nguyên.</summary>
    public List<CommunityChecklistEntryDto> Entries { get; set; } = new();
}

public class CommunityLabelDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class SetCommunityLabelsRequestDto
{
    /// <summary>Tập nhãn mới của truyện (ít nhất một nhãn, nhãn còn hoạt động).</summary>
    public List<int> ContentCategoryIds { get; set; } = new();
}

public class CommunityReviewBlockerDto
{
    /// <summary>own_submission, checklist_incomplete, story_archived, pii_found hoặc no_labels.</summary>
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class CommunityPiiLocationDto
{
    public string Kind { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
}

public class CommunityReviewPackageDto
{
    public int ItemId { get; set; }
    public ReportedStoryDto Story { get; set; } = new();

    /// <summary>Đúng phiên bản đã ghim lúc nộp (không phải phiên bản hiện hành).</summary>
    public ReportedVersionDto Version { get; set; } = new();
    public List<ReportedSceneDto> Scenes { get; set; } = new();
    public List<ReportedVocabularyDto> Vocabulary { get; set; } = new();
    public List<ReportedQuizDto> Quizzes { get; set; } = new();
    public List<ReportedListeningDto> Listening { get; set; } = new();
    public List<CommunityLabelDto> Labels { get; set; } = new();

    /// <summary>AiGenerated hoặc OwnWork (người nộp khai).</summary>
    public string? CopyrightDeclaration { get; set; }

    /// <summary>JSON kết quả dò PII lúc nộp (chỉ loại và vị trí).</summary>
    public string? PiiCheckSummary { get; set; }

    /// <summary>Kết quả dò PII lại trên bản ghim ngay bây giờ (chỉ loại và vị trí).</summary>
    public List<CommunityPiiLocationDto> PiiRescan { get; set; } = new();
    public bool IsResubmission { get; set; }
    public List<CommunityChecklistEntryDto> Checklist { get; set; } = new();
    public List<CommunityReviewBlockerDto> Blockers { get; set; } = new();

    /// <summary>True khi không còn khóa nào (mở nút Approve).</summary>
    public bool CanApprove { get; set; }
    public DateTime ClaimExpiresAt { get; set; }
}

public class RejectCommunityRequestDto
{
    /// <summary>Lý do từ chối (bắt buộc, 1–1000 ký tự).</summary>
    public string Reason { get; set; } = string.Empty;
}

public class CommunityReviewDecisionResultDto
{
    public int ItemId { get; set; }

    /// <summary>"Approved" hoặc "Rejected".</summary>
    public string Decision { get; set; } = string.Empty;
    public int StoryId { get; set; }
    public DateTime DecidedAt { get; set; }
}
