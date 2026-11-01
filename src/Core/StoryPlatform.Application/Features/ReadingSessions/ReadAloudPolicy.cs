namespace StoryPlatform.Application.Features.ReadingSessions;

/// <summary>Luật luyện đọc thành tiếng (Luồng 3 mục 4). Không giới hạn lượt, không thưởng.</summary>
public static class ReadAloudPolicy
{
    /// <summary>Chữ khớp nhưng máy nghe kém tin cậy hơn ngưỡng này thì xếp vào retry.</summary>
    public const double LowConfidence = 0.5;

    public const string UnavailableMessage = "Micro đang bận một chút, em đọc tiếp truyện nhé.";
    public const int MaxAudioBytes = 1_000_000;

    public static readonly IReadOnlySet<string> AudioContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "audio/webm", "audio/ogg", "audio/wav", "audio/x-wav", "audio/wave", "audio/mpeg", "audio/mp3",
        "audio/mp4", "audio/x-m4a", "audio/flac"
    };

    public const string VoiceOffMessage = "Bố mẹ chưa bật đọc bằng giọng nói cho em.";
    public const string EmptyAudioMessage = "Em bấm micro rồi đọc lại nhé.";
    public const string AudioTooLargeMessage = "Đoạn ghi âm dài quá, em đọc ngắn hơn nhé.";
    public const string UnsupportedAudioMessage = "Định dạng ghi âm chưa được hỗ trợ.";
    public const string SessionEndedMessage = "Phiên đọc này đã kết thúc.";

    public static string Message(int good, int total) =>
        good == 0 ? "Em đọc to hơn một chút nhé!"
        : good == total ? "Em đọc hay quá!"
        : "Em đọc hay quá! Thử đọc lại các chữ màu vàng nhé.";

    public static bool IsSupportedContentType(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) && AudioContentTypes.Contains(contentType.Split(';')[0].Trim());
}
