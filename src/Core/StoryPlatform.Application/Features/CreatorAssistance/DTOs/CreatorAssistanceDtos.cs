using System.ComponentModel.DataAnnotations;
using StoryPlatform.Contracts.AI.CreatorAssistance;

namespace StoryPlatform.Application.Features.CreatorAssistance.DTOs;

public sealed record CreateAssistanceTaskRequest
{
    [Required, StringLength(100)] public string OperationId { get; init; } = "";
    [Required] public string EntryPoint { get; init; } = "ContinueWriting";
    public int? BaseVersionId { get; init; }
    [Required] public string RevisionToken { get; init; } = "";
    [Required] public EditorSnapshot EditorSnapshot { get; init; } = new("", "");
    [Required] public AssistanceTarget Target { get; init; } = new("cursor", 0, 0);
    [StringLength(2000)] public string Goal { get; init; } = "";
    public AssistanceConstraints Constraints { get; init; } = new([], []);
    public int? ParentTaskId { get; init; }
    public int? SourceReviewStepId { get; init; }
    public string? FindingId { get; init; }
}
public record AssistanceCommand
{
    [Required, StringLength(100)] public string OperationId { get; init; } = "";
    [Range(1, int.MaxValue)] public int ExpectedTaskRevision { get; init; }
}
public sealed record SuggestAssistanceRequest : AssistanceCommand { public int? AnswerStepId { get; init; } }
public sealed record AnswerAssistanceRequest : AssistanceCommand
{
    public int ClarificationStepId { get; init; }
    public string QuestionId { get; init; } = "";
    public string? ChoiceId { get; init; }
    [StringLength(2000)] public string? CustomAnswer { get; init; }
    public bool LetAiPropose { get; init; }
}
public sealed record ConfirmAssistanceDirectionRequest : AssistanceCommand
{
    public int OptionsStepId { get; init; }
    public string? OptionId { get; init; }
    [StringLength(2000)] public string? DirectionText { get; init; }
    public AssistanceConstraints ConfirmedConstraints { get; init; } = new([], []);
}
public sealed record CreateAssistanceTrialRequest : AssistanceCommand { public int DirectionStepId { get; init; } }
public sealed record CancelAssistanceRequest : AssistanceCommand { public string TargetOperationId { get; init; } = ""; }
public sealed record AssistanceDirection(string ConfirmedDirectionText, AssistanceConstraints Constraints);
public sealed record AssistanceActiveOperation(string OperationId, int StepId, string Status, DateTime? LeaseExpiresAt);
public sealed record AssistanceStepDto(int StepId, int Sequence, string OperationId, string Kind, string Status,
    int ResultingTaskRevision, string? ErrorCode, AssistanceAiResult? Result, AssistanceDirection? Direction,
    DateTime CreatedAt, int? DirectionStepId, string? Answer = null);
public sealed record AssistanceTaskDto(int TaskId, int StoryId, string EntryPoint, string Status, int TaskRevision,
    int? SourceVersionId, string SourceRevisionToken, int AudienceRevision, string SnapshotHash,
    EditorSnapshot Snapshot, AssistanceTarget Target, bool StoryContextStale, bool AudienceContextStale,
    string CurrentStoryRevisionToken, int CurrentAudienceRevision, AssistanceActiveOperation? ActiveOperation,
    int? ActiveClarificationStepId, int? AcceptedAnswerStepId, int? ActiveOptionsStepId, int? ActiveReviewStepId,
    int? ActiveDirectionStepId, int? ActiveTrialStepId, AssistanceStepDto? ActiveDirection, AssistanceStepDto? ActiveTrial,
    int? ParentTaskId, int? SourceReviewStepId, string? FindingId, AssistanceAiResult? ActiveResult = null, string? AcceptedAnswer = null,
    string? Goal = null, AssistanceConstraints? Constraints = null, string? Language = null, string? AgeBand = null);
public sealed record AssistanceTaskSummary(int TaskId, string EntryPoint, string Status, int TaskRevision, DateTime CreatedAt);
public sealed record AssistanceCommandResult(int TaskId, int StepId, int TaskRevision, string Status,
    bool IsRunning, AssistanceStepDto Step, string? PollUrl = null, string? SnapshotHash = null);

public sealed record AssistanceReference(int TaskId, int StepId, string Usage);
public sealed record AssistanceProvenanceReference(int TaskId, int StepId, string Usage, string SnapshotHash,
    string? ResultHash, string? Provider, string? Model, string? PromptVersion);
public sealed record AssistanceProvenance(int SchemaVersion, AssistanceProvenanceReference[] NewReferences,
    int? BaseVersionId, int? RestoredFromVersionId, string? RestoredSourceSummary = null);
