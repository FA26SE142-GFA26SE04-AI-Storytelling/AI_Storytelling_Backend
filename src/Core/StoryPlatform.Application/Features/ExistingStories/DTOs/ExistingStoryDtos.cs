using System.Collections.Generic;

namespace StoryPlatform.Application.Features.ExistingStories.DTOs;

#region Intake

/// <summary>
/// Request nhập nội dung truyện từ Parent/Teacher.
/// Hỗ trợ giai đoạn đầu: paste (text) và txt (UTF-8).
/// DOCX sẽ bổ sung sau khi Paste/TXT ổn định.
/// </summary>
public sealed class ImportStoryRequestDto
{
    /// <summary>Loại đầu vào. Hiện hỗ trợ: <c>paste</c>, <c>txt</c>.</summary>
    public string InputMethod { get; set; } = "paste";

    /// <summary>Nội dung truyện thô (paste) hoặc nội dung file txt.</summary>
    public string? Content { get; set; }

    /// <summary>Tiêu đề truyện (do người dùng đặt).</summary>
    public string? Title { get; set; }

    /// <summary>ID hồ sơ trẻ (đã được validate ở backend).</summary>
    public int ChildProfileId { get; set; }

    /// <summary>Ngôn ngữ (mặc định "vi").</summary>
    public string Language { get; set; } = "vi";

    /// <summary>Idempotency key do client sinh; nếu null backend tự sinh.</summary>
    public string? IdempotencyKey { get; set; }
}

public sealed class ImportStoryResponseDto
{
    public int StoryId { get; set; }
    public int StoryVersionId { get; set; }
    public string StoryStatus { get; set; } = string.Empty;
    public string EditType { get; set; } = string.Empty;
    public int? ContentLength { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = new List<string>();
}

#endregion

#region Evaluation

public enum ExistingStoryDecision
{
    Suitable = 1,
    AdaptRecommended = 2,
    Blocked = 3
}

public sealed class ExistingStoryEvaluationDto
{
    public int StoryId { get; set; }
    public int StoryVersionId { get; set; }
    public ExistingStoryDecision Decision { get; set; }
    public string DecisionText => Decision.ToString();
    public IReadOnlyList<EvaluationIssueDto> HardSafetyIssues { get; set; } = new List<EvaluationIssueDto>();
    public IReadOnlyList<EvaluationIssueDto> ProfileFitIssues { get; set; } = new List<EvaluationIssueDto>();
    public int? RecommendedAgeBand { get; set; }
    public decimal? ReadabilityFkgl { get; set; }
    public decimal? ReadabilityFre { get; set; }
    public int? WordCount { get; set; }
    public bool CanKeepOriginal { get; set; }
}

public sealed class EvaluationIssueDto
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

#endregion

#region Versioning

public sealed class AdaptExistingStoryRequestDto
{
    public int StoryId { get; set; }
    public int BaseStoryVersionId { get; set; }
    /// <summary>Hướng dẫn cho AI (nếu rỗng sẽ dùng guideline mặc định theo profile).</summary>
    public string? Guideline { get; set; }
}

public sealed class ManualEditRequestDto
{
    public int StoryId { get; set; }
    public int BaseStoryVersionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Lesson { get; set; } = string.Empty;
}

public sealed class KeepOriginalRequestDto
{
    public int StoryId { get; set; }
    public int StoryVersionId { get; set; }
    /// <summary>Lý do override bắt buộc (sẽ ghi vào audit log).</summary>
    public string OverrideReason { get; set; } = string.Empty;
}

public sealed class ArchiveExistingStoryRequestDto
{
    public int StoryId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class VersionMutationResponseDto
{
    public int StoryId { get; set; }
    public int StoryVersionId { get; set; }
    public string StoryStatus { get; set; } = string.Empty;
    public string EditType { get; set; } = string.Empty;
    /// <summary>Decision trước khi mutate (nếu có).</summary>
    public string? Decision { get; set; }
}

#endregion
