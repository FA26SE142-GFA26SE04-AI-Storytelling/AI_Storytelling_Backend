using System.ComponentModel.DataAnnotations;

namespace StoryPlatform.Application.Features.CreatorAuthoring.DTOs;

public abstract record CreatorCommand
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string OperationId { get; init; } = string.Empty;
}

public sealed record CreateCreatorDraftRequest : CreatorCommand
{
    [StringLength(200)] public string Title { get; init; } = string.Empty;
    [StringLength(1000)] public string Description { get; init; } = string.Empty;
    [Required, StringLength(10)] public string Language { get; init; } = "vi";
    [Required, StringLength(20)] public string AgeBand { get; init; } = "6-8";
    [StringLength(50)] public string Genre { get; init; } = string.Empty;
    [Required] public string ExperienceMode { get; init; } = "reading";
}

public abstract record CreatorVersionCommand : CreatorCommand
{
    [Required] public string RevisionToken { get; init; } = string.Empty;
    public int? BaseVersionId { get; init; }
}

public sealed record SaveCreatorContentRequest : CreatorVersionCommand
{
    [StringLength(200)] public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    [StringLength(2000)] public string SourceDeclaration { get; init; } = string.Empty;
    [StringLength(2000)] public string RightsDeclaration { get; init; } = string.Empty;
}

public sealed record PreviewCreatorImportRequest : CreatorCommand
{
    [Required] public string RevisionToken { get; init; } = string.Empty;
    [Required] public string Content { get; init; } = string.Empty;
}

public sealed record ConfirmCreatorImportRequest : CreatorVersionCommand
{
    [Required] public string PreviewToken { get; init; } = string.Empty;
    [Required] public string Content { get; init; } = string.Empty;
    [StringLength(200)] public string Title { get; init; } = string.Empty;
    [Required, StringLength(2000)] public string SourceDeclaration { get; init; } = string.Empty;
    [Required, StringLength(2000)] public string RightsDeclaration { get; init; } = string.Empty;
}

public sealed record RestoreCreatorVersionRequest : CreatorVersionCommand
{
    [Range(1, int.MaxValue)] public int SourceVersionId { get; init; }
}

public sealed record CreatorDraftDto(int StoryId, int AuthorUserId, string Title, string Description,
    string Language, string AgeBand, string Genre, string ExperienceMode, int AudienceRevision,
    string RevisionToken, int? SelectedVersionId, int LastVersionNo, DateTime CreatedAt, DateTime? UpdatedAt);

public sealed record CreatorVersionDto(int VersionId, int StoryId, int VersionNo, int? BaseVersionId,
    int? RestoredFromVersionId, int EditorUserId, string Title, string Content, string Origin,
    string SourceDeclaration, string RightsDeclaration, string? ImportFileName, string? ImportSourceHash,
    int AudienceRevision, DateTime CreatedAt);

public sealed record CreatorContentSavedDto(CreatorDraftDto Story, CreatorVersionDto Version);
public sealed record CreatorImportPreviewDto(string PreviewToken, int StoryId, string Content,
    string InputMethod, string? FileName, string SourceHash, DateTime ExpiresAt);
