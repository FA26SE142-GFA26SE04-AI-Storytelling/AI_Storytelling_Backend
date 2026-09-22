namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Phase 5 — sub-scene unit inside a StoryScene used to bind generated TTS audio and word-level timing marks.
/// A scene can have many segments; each segment maps 1-1 to a generated TtsAudio MediaAsset.
/// Offset convention: [StartOffset, EndOffset) — StartOffset inclusive, EndOffset exclusive.
/// TextContent MUST equal SceneText[StartOffset..EndOffset].
/// </summary>
public sealed class StorySegment : BaseEntity
{
    public int StorySceneId { get; set; }
    public StoryScene? StoryScene { get; set; }

    public int SegmentOrder { get; set; }          // 1-based, contiguous within a scene
    public int StartOffset { get; set; }           // inclusive character offset in SceneText
    public int EndOffset { get; set; }             // exclusive
    public string TextContent { get; set; } = string.Empty;
}
