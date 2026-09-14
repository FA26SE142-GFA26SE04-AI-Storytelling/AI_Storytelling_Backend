using System.ComponentModel.DataAnnotations;

namespace StoryPlatform.Application.Features.Outline.DTOs;

public sealed class OutlineProgressDto
{
    public int StoryId { get; init; }
    public string StoryStatus { get; init; } = string.Empty;
    public OutlineVersionDto? CurrentVersion { get; init; }
    public string? ActiveOperation { get; init; }
    public string? ActiveJobStatus { get; init; }
    public string? LastErrorCode { get; init; }
}

public sealed class OutlineVersionDto
{
    public int Id { get; init; }
    public int VersionNo { get; init; }
    public string EditType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Opening { get; init; } = string.Empty;
    public string Development { get; init; } = string.Empty;
    public string Ending { get; init; } = string.Empty;
    public bool IsCurrent { get; init; }
    public int? EditorUserId { get; init; }
    public int? OutlineApprovedByUserId { get; init; }
    public DateTime? OutlineApprovedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class EditOutlineRequestDto
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get; init; } = string.Empty;

    [Required, StringLength(4_000, MinimumLength = 1)]
    public string Opening { get; init; } = string.Empty;

    [Required, StringLength(4_000, MinimumLength = 1)]
    public string Development { get; init; } = string.Empty;

    [Required, StringLength(4_000, MinimumLength = 1)]
    public string Ending { get; init; } = string.Empty;
}

public sealed class RegenerateOutlineRequestDto
{
    [Required, StringLength(100, MinimumLength = 8)]
    public string OperationKey { get; init; } = string.Empty;
}

public sealed class RetryOutlineRequestDto
{
    [Required, StringLength(100, MinimumLength = 8)]
    public string OperationKey { get; init; } = string.Empty;
}

public sealed class ApproveOutlineRequestDto
{
    [Required, StringLength(100, MinimumLength = 8)]
    public string ApprovalKey { get; init; } = string.Empty;
}

public sealed class RejectOutlineRequestDto
{
    [Required, StringLength(500, MinimumLength = 2)]
    public string Reason { get; init; } = string.Empty;
}
