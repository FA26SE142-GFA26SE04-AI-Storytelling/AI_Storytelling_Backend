using StoryPlatform.Application.Features.ChildProfiles.ReadingTime.DTOs;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.ReadingTime.Interfaces;

/// <summary>Phụ huynh (chủ hồ sơ) xem / lưu cấu hình "Thời gian &amp; thói quen".</summary>
public interface IReadingScheduleService
{
    Task<ReadingScheduleDto> GetAsync(int childProfileId, int currentUserId, CancellationToken cancellationToken = default);

    /// <summary>Gợi ý theo nhóm tuổi khi bé chưa có hồ sơ (form "Thêm bé"); không đọc / ghi DB, childProfileId = 0.</summary>
    ReadingScheduleDto GetSuggestion(AgeBand ageBand);

    Task<ReadingScheduleDto> SetAsync(
        int childProfileId, int currentUserId, SetReadingScheduleRequestDto request,
        CancellationToken cancellationToken = default);
}

/// <summary>Cổng kiểm tra cho các lối vào phiên bé và phiên đọc.</summary>
public interface IReadingTimeGate
{
    /// <summary>Ném ForbiddenException (READING_TIME_*) khi bé không được vào / mở truyện lúc này.</summary>
    /// <param name="childFacing">true: câu báo cho bé; false: câu báo cho phụ huynh.</param>
    Task EnsureCanStartAsync(int childProfileId, bool childFacing, CancellationToken cancellationToken = default);

    /// <summary>Trạng thái hiện tại, chỉ đọc.</summary>
    Task<ReadingTimeStatusDto> GetStatusAsync(int childProfileId, CancellationToken cancellationToken = default);

    /// <summary>Gọi từ signal của phiên đọc đang mở: cập nhật đếm nghỉ mắt rồi trả trạng thái (có xét gia hạn).</summary>
    Task<ReadingTimeStatusDto> TrackReadingAsync(
        int childProfileId, DateTime readingStartedAtUtc, CancellationToken cancellationToken = default);
}

/// <summary>Middleware JWT gọi khi ghi hoạt động phiên bé (tối đa 1 lần/phút). Không SaveChanges; caller tự lưu.</summary>
public interface IScreenTimeRecorder
{
    Task RecordActivityAsync(
        int childProfileId, DateTime previousActivityUtc, DateTime nowUtc, CancellationToken cancellationToken = default);
}
