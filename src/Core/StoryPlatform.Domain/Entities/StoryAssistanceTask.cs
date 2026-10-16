namespace StoryPlatform.Domain.Entities;

public sealed class StoryAssistanceTask : BaseEntity
{
    public int StoryId { get; set; }
    public int OwnerUserId { get; set; }
    public string CreateOperationId { get; set; } = "";
    public string CreateRequestHash { get; set; } = "";
    public string CreateResponseJson { get; set; } = "";
    public string EntryPoint { get; set; } = "";
    public int? SourceVersionId { get; set; }
    public string SourceRevisionToken { get; set; } = "";
    public int AudienceRevision { get; set; }
    public string Language { get; set; } = "";
    public string AgeBand { get; set; } = "";
    public string ContextJson { get; set; } = "";
    public string SnapshotHash { get; set; } = "";
    public int TaskRevision { get; set; } = 1;
    public string Status { get; set; } = "Open";
    public int LastSequence { get; set; }
    public int? ParentTaskId { get; set; }
    public int? SourceReviewStepId { get; set; }
    public string? FindingId { get; set; }
    public string? ActiveOperationId { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public int? ActiveClarificationStepId { get; set; }
    public int? AcceptedAnswerStepId { get; set; }
    public int? ActiveOptionsStepId { get; set; }
    public int? ActiveReviewStepId { get; set; }
    public int? ActiveDirectionStepId { get; set; }
    public int? ActiveTrialStepId { get; set; }
}
