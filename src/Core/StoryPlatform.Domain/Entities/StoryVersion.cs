using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class StoryVersion : BaseEntity
{
    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int VersionNo { get; set; }
    public VersionEditType EditType { get; set; } = VersionEditType.Initial;

    public int? EditorUserId { get; set; }
    public virtual UserAccount? EditorUser { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? OutlineOpening { get; set; }
    public string? OutlineDevelopment { get; set; }
    public string? OutlineEnding { get; set; }
    public string? Content { get; set; }
    public string? Lesson { get; set; }
    public decimal? ReadabilityFkgl { get; set; }
    public decimal? ReadabilityFre { get; set; }
    public decimal? SafetyScore { get; set; }
    public bool IsCurrent { get; set; } = false;
}
