namespace StoryPlatform.Domain.Entities;

public sealed class StoryAssistanceStep : BaseEntity
{
    public int TaskId { get; set; }
    public int Sequence { get; set; }
    public string OperationId { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "Running";
    public string InputJson { get; set; } = "";
    public string? OutputJson { get; set; }
    public string? ResponseJson { get; set; }
    public int ResultingTaskRevision { get; set; }
    public int? DirectionStepId { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime? CompletedAt { get; set; }
}
