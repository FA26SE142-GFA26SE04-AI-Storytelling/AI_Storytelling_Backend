namespace StoryPlatform.Application.Features.ReadingSessions;

/// <summary>Cảm nhận sau khi đọc: cảm xúc, cảnh yêu thích, lời bé (chữ/ghi âm). Không dùng AI, chỉ chủ hồ sơ xem.</summary>
public static class StoryReflectionPolicy
{
    public const int MaxEmotions = 2;
    public const int MaxNoteTextLength = 200;
    public const string StorageFolder = "story-reflections";
    public static readonly TimeSpan ListenUrlLifetime = TimeSpan.FromMinutes(15);

    public const string ChooseEmotionMessage = "Em chọn một hoặc hai cảm xúc nhé.";
    public const string TooManyEmotionsMessage = "Em chọn tối đa hai cảm xúc thôi nhé.";
    public const string SceneNotInStoryMessage = "Cảnh em chọn không thuộc truyện này.";
    public const string ChooseSceneFirstMessage = "Em chọn cảnh em thích nhất trước nhé.";
    public const string SceneChangedMessage = "Em vừa đổi cảnh khác, em gửi lại lời cho cảnh mới nhé.";
    public const string EmptyNoteMessage = "Em viết hoặc kể cho bố mẹ nghe nhé.";
    public const string NoteTooLongMessage = "Em viết ngắn hơn một chút nhé.";
    public const string NoteSentMessage = "Đã gửi cho bố mẹ";

    /// <summary>Cắt khoảng trắng hai đầu; chuỗi rỗng → null.</summary>
    public static string? NormalizeNote(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
