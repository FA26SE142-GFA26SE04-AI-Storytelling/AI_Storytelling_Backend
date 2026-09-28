namespace StoryPlatform.Domain.Entities;

/// <summary>
/// One ordered visual moment within a canonical StoryScene. Offsets use [start, end)
/// relative to SceneText and do not alter the text or its TTS segments.
/// </summary>
public sealed class IllustrationBeat : BaseEntity
{
    public int StorySceneId { get; set; }
    public StoryScene? StoryScene { get; set; }
    public int BeatOrder { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public string VisualFocus { get; set; } = string.Empty;
}
