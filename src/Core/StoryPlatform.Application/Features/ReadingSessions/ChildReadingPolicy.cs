using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions;

public static class ChildReadingPolicy
{
    public const string StoryUnavailableMessage = "Truyện này chưa mở cho em, mình đọc truyện khác nhé!";
    public const string LibraryLockedMessage = "Thư viện đang nghỉ một chút. Em đọc lại truyện đang mở nhé!";
    public const string ProfileUnavailableMessage = "Hồ sơ này hiện chưa đọc truyện được.";

    public const int MaxAddedSecondsPerSave = 600;
    public const int MaxEventPayloadLength = 2000;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    /// <summary>Chỉ những sự kiện này app của trẻ được tự gửi; phần còn lại do server ghi.</summary>
    public static readonly IReadOnlySet<TelemetryEventType> ClientEvents = new HashSet<TelemetryEventType>
    {
        TelemetryEventType.PageCompleted,
        TelemetryEventType.TtsStarted,
        TelemetryEventType.TtsCompleted,
        TelemetryEventType.AsrWordResult,
        TelemetryEventType.PronunciationError,
        TelemetryEventType.LibrarySearched
    };

    public static bool IsActive(SessionStatus status) =>
        status is SessionStatus.Started or SessionStatus.Reading or SessionStatus.Activity;
}
