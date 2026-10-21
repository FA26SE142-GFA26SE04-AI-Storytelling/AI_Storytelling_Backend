using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public sealed class ModerationRubricVersion : BaseEntity
{
    public string Code { get; set; } = "gray-zone";
    public string VersionNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public string DefinitionJson { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public ModerationRubricStatus Status { get; set; }
    public int Revision { get; set; } = 1;
    public int CreatedByUserId { get; set; }
    public int? ActivatedByUserId { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? RetiredAt { get; set; }
}
