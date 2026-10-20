using System.ComponentModel.DataAnnotations;
namespace StoryPlatform.Application.Features.Moderation.DTOs;
public sealed record CreatorRemovalRequest
{
    [Required, StringLength(2000, MinimumLength = 1)] public string Reason { get; init; } = string.Empty;
}
public sealed record CreatorRevisionRequest
{
    [Range(1, int.MaxValue)] public int SourceVersionId { get; init; }
}
public sealed record RemovalDecisionRequest
{
    public bool Approve { get; init; }
    [Required, StringLength(2000, MinimumLength = 1)] public string Reason { get; init; } = string.Empty;
    [Range(0, int.MaxValue)] public int RowVersion { get; init; }
}
public sealed record CreatorPublicationResult(int StoryId, int VersionId, int? ItemId, string Status, string RevisionToken);

public sealed record CreatorPublicationStatus(int StoryId, int VersionId, bool IsPublished, string ReviewStatus, int? OpenItemId, string? OpenItemType, string? OpenItemStatus);
