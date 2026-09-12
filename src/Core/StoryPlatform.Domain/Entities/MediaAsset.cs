using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class MediaAsset : BaseEntity
{
    public int StoryVersionId { get; set; }
    public virtual StoryVersion? StoryVersion { get; set; }

    public MediaType Type { get; set; }
    public MediaStatus Status { get; set; } = MediaStatus.Queued;
    public int? SceneIndex { get; set; }
    public string? Url { get; set; }
}
