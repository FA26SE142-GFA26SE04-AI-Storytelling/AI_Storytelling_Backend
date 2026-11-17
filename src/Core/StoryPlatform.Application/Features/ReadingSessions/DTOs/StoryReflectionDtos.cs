namespace StoryPlatform.Application.Features.ReadingSessions;

public sealed class StoryEmotionDto
{
    /// <summary>Tên StoryEmotion: "Happy", "Funny", "Surprised", "Excited", "Touched", "Sad", "Scared".</summary>
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class SetStoryEmotionsRequestDto
{
    public List<string>? Emotions { get; set; }
}

public sealed class SetFavoriteSceneRequestDto
{
    /// <summary>scenes[].sceneId của màn đọc.</summary>
    public int SceneId { get; set; }
}

/// <summary>Lời bé về cảnh yêu thích; Audio rỗng = không ghi âm.</summary>
public sealed class StoryReflectionNoteRequest
{
    public string? Text { get; init; }
    public byte[] Audio { get; init; } = [];
    public string? ContentType { get; init; }
}

public sealed class ChildStoryReflectionDto
{
    public int SessionId { get; set; }
    public List<StoryEmotionDto> Emotions { get; set; } = new();
    public int? FavoriteSceneId { get; set; }
    public string? NoteText { get; set; }
    public bool HasNoteRecording { get; set; }
    /// <summary>true khi bố mẹ đã bật giọng nói: FE hiện nút micro; false thì chỉ cho viết.</summary>
    public bool CanRecord { get; set; }
    /// <summary>Lời khen/ xác nhận ngắn (chỉ có khi vừa gửi lời bé).</summary>
    public string? Message { get; set; }
}

public sealed class ParentStoryReflectionDto
{
    public int SessionId { get; set; }
    public List<StoryEmotionDto> Emotions { get; set; } = new();
    /// <summary>null khi bé chưa chọn cảnh hoặc cảnh không còn.</summary>
    public ParentFavoriteSceneDto? FavoriteScene { get; set; }
    public string? NoteText { get; set; }
    /// <summary>Link nghe lời bé, hết hạn sau 15 phút; null khi bé không ghi âm.</summary>
    public string? NoteAudioUrl { get; set; }
    public DateTime? NoteSentAt { get; set; }
}

public sealed class ParentFavoriteSceneDto
{
    public int SceneId { get; set; }
    public int SceneIndex { get; set; }
    public string SceneText { get; set; } = string.Empty;
    /// <summary>Tấm tranh bé đã thấy (link đã ký); null khi cảnh không có tranh.</summary>
    public string? IllustrationUrl { get; set; }
}
