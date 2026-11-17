namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Cảm nhận sau khi đọc của trẻ cho một phiên: cảm xúc (1–2), cảnh thích nhất và lời trẻ về cảnh đó (chữ và/hoặc ghi âm).
/// Chỉ chủ hồ sơ xem. File lưu tới khi chủ hồ sơ xóa. Không phân tích bằng AI. Mức "thích" vẫn ở ReadingSession.Feeling.
/// </summary>
public class StoryReflection : BaseEntity
{
    public int ReadingSessionId { get; set; }
    public virtual ReadingSession? ReadingSession { get; set; }

    public int ChildProfileId { get; set; }
    public int StoryId { get; set; }
    public int? StoryVersionId { get; set; }

    /// <summary>Tên StoryEmotion, 0–2 phần tử, không trùng.</summary>
    public List<string> Emotions { get; set; } = new();

    public int? FavoriteSceneId { get; set; }
    public virtual StoryScene? FavoriteScene { get; set; }

    /// <summary>Đúng tấm tranh bé thấy ở cảnh yêu thích lúc chọn; null khi cảnh không có tranh.</summary>
    public int? FavoriteMediaAssetId { get; set; }
    public virtual MediaAsset? FavoriteMediaAsset { get; set; }

    /// <summary>Chữ bé viết về cảnh (≤ 200 ký tự); null khi không viết.</summary>
    public string? NoteText { get; set; }
    public string? NoteAudioStoragePath { get; set; }
    public string? NoteAudioContentType { get; set; }
    public int? NoteAudioBytes { get; set; }
    public DateTime? NoteSentAt { get; set; }
}
