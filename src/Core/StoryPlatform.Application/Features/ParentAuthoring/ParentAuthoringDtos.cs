using System.ComponentModel.DataAnnotations;
using StoryPlatform.Application.Features.CreatorAuthoring.DTOs;

namespace StoryPlatform.Application.Features.ParentAuthoring;

public sealed record CreateParentDraftRequest : CreatorCommand
{
    [Range(1, int.MaxValue)] public int ChildProfileId { get; init; }
    [StringLength(200)] public string Title { get; init; } = "Truyện của gia đình";
    public string? Language { get; init; }
    public string? Purpose { get; init; }
    public string ExperienceMode { get; init; } = "reading";
}
public sealed record ParentDraftDto(CreatorDraftDto Story, int ChildProfileId, string Purpose,
    bool IsReadOnly = false, string? ReadOnlyReason = null,
    bool CanUseAiAssistance = false, string? AiUnavailableReason = null);
public sealed record ParentSetupRequest : CreatorVersionCommand
{
    public string? Purpose { get; init; }
    public string? Language { get; init; }
    public string? ExperienceMode { get; init; }
}
public sealed record ParentCheckRequest : CreatorVersionCommand;
public sealed record ParentChecklist(bool IntendedMeaning, bool SuitableForChild, bool PrivacyChecked, bool EndingChecked);
public sealed record ParentContentConfirmRequest : CreatorVersionCommand
{
    public int CheckId { get; init; }
    public ParentChecklist? Checklist { get; init; }
}
public sealed record ParentPrepareRequest : CreatorVersionCommand
{
    public int ContentConfirmationId { get; init; }
    // UTF-16 exclusive ends. Null uses the shared deterministic paragraph splitter.
    public int[]? SegmentEnds { get; init; }
}
public sealed record ParentMediaGenerateRequest : CreatorVersionCommand
{
    [Range(1, int.MaxValue)] public int ContentConfirmationId { get; init; }
}
public sealed record ParentMediaGenerationDto(int StoryId, int StoryVersionId, int JobId, int PackageRevision,
    string ProgressUrl, string PackageUrl);
public sealed record ParentFamilyConfirmRequest : CreatorVersionCommand
{
    public int PreparationId { get; init; }
}
public sealed record ParentFamilyRevokeRequest : CreatorVersionCommand
{
    public int ConfirmationId { get; init; }
}
public sealed record ParentCheckDto(int CheckId, int StoryVersionId, string ContextFingerprint,
    bool CanPrepare, string[] Errors, string[] Warnings, string Coverage);
public sealed record ParentContentConfirmationDto(int ConfirmationId, int StoryVersionId, int CheckId, ParentChecklist Checklist);
public sealed record ParentSegmentDto(int Order, int StartOffset, int EndOffset, string Text);
public sealed record ParentPreparationDto(int PreparationId, int StoryId, int StoryVersionId, int ChildProfileId, int AudienceRevision,
    string ContextFingerprint, string Title, string Language, string ExperienceMode,
    ParentSegmentDto[] Segments, bool IsReady, string[] MissingRequirements)
{
    public int? MediaPackageRevision { get; init; }
    public StoryPlatform.Application.Features.MediaGeneration.Models.StoryMediaPackage? Media { get; init; }
}
public sealed record ParentFamilyConfirmationDto(int ConfirmationId, int StoryId, int StoryVersionId,
    int ChildProfileId, int PreparationId, DateTime ConfirmedAt);
public sealed record ParentOperationDto(int Id, string Kind, int? StoryVersionId, bool SourceIsCurrent,
    DateTime? RevokedAt, System.Text.Json.JsonElement Result);
