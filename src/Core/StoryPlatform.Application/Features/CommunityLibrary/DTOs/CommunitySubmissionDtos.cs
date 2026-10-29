using System.Text.Json.Serialization;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.CommunityLibrary;

/// <summary>Mã lý do không nộp được (ổn định để giao diện dựa vào).</summary>
public static class CommunityBlockerCodes
{
    public const string StoryNotPrivate = "story_not_private";
    public const string StoryArchived = "story_archived";
    public const string StoryNotReady = "story_not_ready";
    public const string NoCurrentVersion = "no_current_version";
    public const string OpenReport = "open_report";
    public const string AlreadySubmitted = "already_submitted";
    public const string CopyrightRequired = "copyright_required";
    public const string FamilyUse = "family_use";
    public const string PiiFound = "pii_found";
}

public class CommunitySubmitRequestDto
{
    public int StoryId { get; set; }

    /// <summary>Xác nhận truyện không chứa thông tin định danh của bé / gia đình (bắt buộc khi nộp).</summary>
    public bool ConfirmNoPersonalInfo { get; set; }

    /// <summary>
    /// Truyện tự viết / nhập phải khai OwnWork ("Đây là tác phẩm do tôi sáng tác"); truyện AI của nền tảng sinh không
    /// cần khai. FamilyUse (chỉ dùng trong gia đình) không nộp được.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CopyrightDeclaration? CopyrightDeclaration { get; set; }
}

public class CommunityBlockerDto
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class PiiFindingDto
{
    /// <summary>phone, email, url, social_handle, address, school, class hoặc child_nickname.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>title, description, content, lesson, scene N, vocabulary N, quiz N hoặc discussion N.</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Đoạn ngắn quanh chỗ nghi để tự tìm và sửa (chỉ trả cho người nộp, không được lưu).</summary>
    public string Excerpt { get; set; } = string.Empty;
}

public class CommunityEligibilityDto
{
    public int StoryId { get; set; }
    public bool CanSubmit { get; set; }

    /// <summary>Phiên bản sẽ được ghim (phiên bản hiện hành); null khi không có.</summary>
    public int? StoryVersionId { get; set; }

    /// <summary>"none" (truyện AI) hoặc "own_work" (truyện tự viết / nhập).</summary>
    public string RequiredDeclaration { get; set; } = "none";
    public List<CommunityBlockerDto> Blockers { get; set; } = new();
    public List<PiiFindingDto> PiiFindings { get; set; } = new();
}

public class CommunitySubmitResultDto
{
    public int ItemId { get; set; }
    public int StoryId { get; set; }

    /// <summary>Phiên bản đã được ghim cho thư viện.</summary>
    public int StoryVersionId { get; set; }

    /// <summary>"Pending" sau khi nộp.</summary>
    public string Status { get; set; } = string.Empty;
}

public class CommunitySubmissionDto
{
    public int ItemId { get; set; }
    public int StoryId { get; set; }
    public string? StoryTitle { get; set; }

    /// <summary>"Pending" (kể cả đã có người nhận), "Approved", "Rejected", "Withdrawn" hoặc "Removed".</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }

    /// <summary>Lý do Moderator từ chối (chỉ khi Rejected); không nêu người duyệt.</summary>
    public string? RejectReason { get; set; }
    public bool CanWithdraw { get; set; }
    public bool CanRemove { get; set; }
}
