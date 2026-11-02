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
    public const string NoFlagsMessage = "Em chọn ít nhất một chỗ nhé.";
    public const string TooManyFlagsMessage = "Em chọn tối đa 5 chỗ nhé.";
    public const string SceneNotInStoryMessage = "Chỗ em chọn không thuộc truyện này.";
    public const string AlreadyFlaggedMessage = "Em đã gửi những chỗ thắc mắc cho bố mẹ rồi.";

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
