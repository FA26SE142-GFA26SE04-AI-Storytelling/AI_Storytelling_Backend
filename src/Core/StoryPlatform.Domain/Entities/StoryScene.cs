namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Immutable mapping from an approved story version to one canonical media scene.
/// SceneText must equal Content[TextRangeStart..TextRangeEnd].
/// </summary>
public sealed class StoryScene : BaseEntity
{
    public int StoryVersionId { get; set; }
    public StoryVersion? StoryVersion { get; set; }
    public int SceneIndex { get; set; }
    public int TextRangeStart { get; set; }
    public int TextRangeEnd { get; set; }
    public string SceneText { get; set; } = string.Empty;
    public string? VisualDescription { get; set; }
}
