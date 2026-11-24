using StoryPlatform.Application.Features.ChildProfiles.ReadingTime.DTOs;

namespace StoryPlatform.Application.Features.ReadingSessions;

public static class ReadingSessionInterruptPolicy
{
    public static readonly TimeSpan PostReadWindow = TimeSpan.FromMinutes(30);
    public const string ReturnToJourneyMap = "JourneyMap";
    public const string ChildMessage = "Truyện này đang nghỉ một chút, mình đọc truyện khác nhé!";
}

public class ForceExitSignalDto
{
    public bool ShouldExit { get; set; }
    public string? ReturnTo { get; set; }
    public string? Message { get; set; }
    public string SessionStatus { get; set; } = string.Empty;
    /// <summary>Trạng thái "Thời gian &amp; thói quen"; null khi không có cổng kiểm tra (không bật "Thời gian &amp; thói quen") hoặc phiên bị phụ huynh ngắt. Phiên đã thoát vì hết giờ ở lần trước vẫn trả trạng thái hiện tại.</summary>
    public ReadingTimeStatusDto? ReadingTime { get; set; }
}
