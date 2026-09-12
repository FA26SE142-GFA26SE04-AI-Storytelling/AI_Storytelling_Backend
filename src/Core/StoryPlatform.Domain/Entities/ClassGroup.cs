using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class ClassGroup : BaseEntity
{
    public int TeacherUserId { get; set; }
    public virtual UserAccount? TeacherUser { get; set; }

    public string Name { get; set; } = string.Empty;
    public int KnowledgeTreeExp { get; set; } = 0;
    public ClassGroupStatus Status { get; set; } = ClassGroupStatus.Active;

    public int? OrganizationId { get; set; }
    public virtual Organization? Organization { get; set; }
}
