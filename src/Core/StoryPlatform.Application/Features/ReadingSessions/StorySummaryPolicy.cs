namespace StoryPlatform.Application.Features.ReadingSessions;

/// <summary>Tóm tắt cuối truyện gửi bố mẹ (Luồng 3 — Danh sách tương tác của trẻ, mục 6). Không dùng AI, không chấm.</summary>
public static class StorySummaryPolicy
{
    /// <summary>Khoảng 2–3 phút giọng nói dạng webm/opus.</summary>
    public const int MaxAudioBytes = 3_000_000;
    public const int MaxFlags = 5;
    public static readonly TimeSpan ListenUrlLifetime = TimeSpan.FromMinutes(15);

    public const string SentMessage = "Đã gửi cho bố mẹ";
    public const string EmptyAudioMessage = "Em bấm micro rồi kể lại nhé.";
    public const string AudioTooLargeMessage = "Đoạn ghi âm dài quá, em kể ngắn hơn nhé.";
    public const string AlreadyRecordedMessage = "Em đã gửi bản ghi âm cho bố mẹ rồi.";
    public const string SceneNotInStoryMessage = "Chỗ em chọn không thuộc truyện này.";

    public const int MaxQuestionTextLength = 200;

    public const string TooManyQuestionsMessage = "Em hỏi tối đa 5 câu cho mỗi lần đọc nhé.";
    public const string AlreadyAskedMessage = "Em đã hỏi bố mẹ về chỗ này rồi.";
    public const string ChoosePictureMessage = "Em chọn bức tranh em thắc mắc nhé.";
    public const string PictureNotInSceneMessage = "Bức tranh em chọn không có trong cảnh này.";
    public const string QuestionTooLongMessage = "Câu hỏi dài quá, em viết ngắn hơn nhé.";

    /// <summary>Cắt khoảng trắng hai đầu; chuỗi rỗng → null.</summary>
    public static string? NormalizeQuestionText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public static string Extension(string contentType) => contentType switch
    {
        "audio/webm" => ".webm",
        "audio/ogg" => ".ogg",
        "audio/wav" or "audio/x-wav" or "audio/wave" => ".wav",
        "audio/mpeg" or "audio/mp3" => ".mp3",
        "audio/mp4" or "audio/x-m4a" => ".m4a",
        "audio/flac" => ".flac",
        _ => ".bin"
    };
}
