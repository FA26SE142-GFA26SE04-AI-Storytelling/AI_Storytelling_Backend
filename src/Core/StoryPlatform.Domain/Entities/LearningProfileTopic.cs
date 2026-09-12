using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class LearningProfileTopic : BaseEntity
{
    public int LearningProfileId { get; set; }
    public virtual LearningProfile? LearningProfile { get; set; }

    public string Topic { get; set; } = string.Empty;
    public TopicRelation Relation { get; set; }
}
