namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Versioned continuity context used by every media artifact of a story version.
/// Revisions are append-only so retries cannot silently replace prior context.
/// </summary>
public sealed class MediaContext : BaseEntity
{
    public int StoryVersionId { get; set; }
    public StoryVersion? StoryVersion { get; set; }
    public int Revision { get; set; }
    public string ContextJson { get; set; } = "{}";
}
