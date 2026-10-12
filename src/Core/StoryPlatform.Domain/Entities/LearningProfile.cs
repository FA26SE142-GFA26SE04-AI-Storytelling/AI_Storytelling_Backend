using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class LearningProfile : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int ReadingLevel { get; set; }
    public ReadingMode ReadingMode { get; set; } = ReadingMode.ListenFirst;
    public string? ComprehensionGoal { get; set; }
}
