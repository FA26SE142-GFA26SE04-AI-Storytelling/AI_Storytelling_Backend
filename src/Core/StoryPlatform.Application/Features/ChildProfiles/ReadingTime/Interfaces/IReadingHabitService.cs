using StoryPlatform.Application.Features.ChildProfiles.ReadingTime.DTOs;

namespace StoryPlatform.Application.Features.ChildProfiles.ReadingTime.Interfaces;

/// <summary>Thói quen đọc: tiến độ tuần, chuỗi ngày đọc, lịch nhắc. Chỉ đọc DB, không ghi.</summary>
public interface IReadingHabitService
{
    /// <summary>Tiến độ cho phụ huynh; <paramref name="weeklyGoalDays"/> null thì không bao giờ đạt mục tiêu.</summary>
    Task<ReadingHabitProgressDto> GetProgressAsync(
        int childProfileId, int? weeklyGoalDays, CancellationToken cancellationToken = default);

    /// <summary>Khối habit của /me: tiến độ + lịch nhắc 7 ngày + giờ yên lặng (rỗng khi chưa bật thói quen).</summary>
    Task<ChildHabitDto> GetChildHabitAsync(int childProfileId, CancellationToken cancellationToken = default);
}
