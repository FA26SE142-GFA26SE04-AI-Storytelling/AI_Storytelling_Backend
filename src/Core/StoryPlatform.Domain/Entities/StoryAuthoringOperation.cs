namespace StoryPlatform.Domain.Entities;

/// <summary>Durable command receipt for replay of a content mutation, not a family delivery grant.</summary>
public sealed class StoryAuthoringOperation : BaseEntity
{
    public int StoryId { get; set; }
    public int StoryVersionId { get; set; }
    public StoryVersion? StoryVersion { get; set; }
    public int ActorUserId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string OperationResponseJson { get; set; } = string.Empty;
}
