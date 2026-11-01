using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Một tương tác lúc nghe gắn vào cuối một đoạn văn của version đã chốt.
/// Cột không thuộc Type phải để null; ListeningInteractionRules kiểm tra khi ghi.
/// </summary>
public class ListeningInteraction : BaseEntity
{
    public int StoryVersionId { get; set; }
    public virtual StoryVersion? StoryVersion { get; set; }

    public ListeningInteractionType Type { get; set; }
    /// <summary>Vị trí ký tự kết thúc của đoạn neo trong StoryVersion.Content.</summary>
    public int AnchorOffset { get; set; }
    /// <summary>Null tới khi Phase 5 gắn scene.</summary>
    public int? StorySceneId { get; set; }
    public virtual StoryScene? StoryScene { get; set; }

    public CheckpointKind? Kind { get; set; }
    public string? Prompt { get; set; }
    public string? SourceQuote { get; set; }
    public int? SourceStart { get; set; }
    public int? SourceEnd { get; set; }
    public string? CharacterName { get; set; }
    public string? HiddenWord { get; set; }
    public string? Hint { get; set; }
    /// <summary>PicturePrediction: vị trí kết thúc của đoạn có đáp án.</summary>
    public int? RevealOffset { get; set; }
    public string? Explanation { get; set; }
    public string? MatchFeedback { get; set; }
    public string? NoMatchFeedback { get; set; }
    public string? WaitFeedback { get; set; }
}
