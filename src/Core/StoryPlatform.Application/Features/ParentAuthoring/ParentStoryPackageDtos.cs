using System.ComponentModel.DataAnnotations;
using StoryPlatform.Application.Features.CreatorAuthoring.DTOs;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.Application.Features.ParentAuthoring;

public sealed record ParentPackageComponents(bool Vocabulary = false, bool Quiz = false, bool Listening = false,
    bool Images = false, bool Narration = false);
public sealed record ParentPackageRecipient([Range(1, int.MaxValue)] int ChildProfileId, ParentPackageComponents Components);
public sealed record CreateParentPackagesRequest : CreatorVersionCommand
{
    [Required, MinLength(1), MaxLength(100)] public ParentPackageRecipient[] Recipients { get; init; } = [];
}
public record ParentPackageCommand : CreatorCommand
{
    [Required] public string PackageToken { get; init; } = "";
}
public sealed record ParentPackageRetryRequest : ParentPackageCommand
{
    public string[] Components { get; init; } = [];
}
public sealed record ParentPackageReviseRequest : ParentPackageCommand
{
    [Required] public ParentPackageComponents Components { get; init; } = new();
    public string[] Regenerate { get; init; } = [];
}
public sealed record ParentPackageFinalizeRequest : CreatorVersionCommand;
public sealed record ParentPackageBatchGrantRequest : CreatorCommand
{
    [Required, MinLength(1), MaxLength(100)] public ParentPackageGrantItem[] Packages { get; init; } = [];
}
public sealed record ParentPackageGrantItem([Range(1, int.MaxValue)] int PackageId, [Required] string PackageToken);
public sealed record ParentContentFinalizationDto(int StoryId, int StoryVersionId, string ContentHash, bool IsFinalized);
public sealed record ParentPackageCheck(bool Passed, string[] Errors, decimal? SafetyScore = null,
    string? SafetyRuleVersion = null, string[]? Evidence = null, string? ReasonCode = null,
    string[]? Violations = null, StoryPlatform.Contracts.AI.Models.GenerationMetadataDto? Metadata = null);
public sealed record ParentPackageManifest(int[] VocabularyIds, int[] QuizIds, int[] ListeningIds, int[] SceneIds,
    int[] SegmentIds, int[] MediaAssetIds)
{
    public static ParentPackageManifest Empty => new([], [], [], [], [], []);
}
public sealed record ParentPackageDto(int PackageId, int StoryId, int StoryVersionId, int ChildProfileId, int Revision,
    string PackageToken, string Status, ParentPackageComponents Components, ParentPackageCheck? Check,
    string? LastErrorCode, DateTime? ConfirmedAt)
{
    public IReadOnlyDictionary<string, string> ComponentStatus { get; init; } = new Dictionary<string, string>();
    public int VocabularyCount { get; init; }
    public int QuizCount { get; init; }
    public int ListeningCount { get; init; }
}
public sealed record ParentPackagePreviewDto(ParentPackageDto Package, string Title, string Content,
    ParentPackageManifest Manifest, object Vocabulary, object Quiz, object Listening, object Media);
public sealed record ParentPackageWorkState
{
    public string[] Regenerate { get; init; } = [];
    public string[] Completed { get; set; } = [];
    public Dictionary<string, StoryPlatform.Application.Features.ListeningInteractions.Models.ListeningJobState> ListeningStates { get; set; } = new();
}
