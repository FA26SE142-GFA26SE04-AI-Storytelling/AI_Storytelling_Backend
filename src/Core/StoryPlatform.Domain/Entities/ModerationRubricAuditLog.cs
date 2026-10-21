namespace StoryPlatform.Domain.Entities;

public sealed class ModerationRubricAuditLog : BaseEntity
{
    public int RubricVersionId { get; set; }
    public ModerationRubricVersion RubricVersion { get; set; } = null!;
    public int ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
}
