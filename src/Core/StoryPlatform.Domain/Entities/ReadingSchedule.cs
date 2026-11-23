using System;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Cấu hình "Thời gian &amp; thói quen" của một hồ sơ trẻ: khung giờ được dùng app (reading_schedule_windows),
/// giờ đi ngủ, nghỉ mắt bắt buộc, giới hạn thời gian màn hình. Chưa có dòng = tắt.
/// </summary>
public class ReadingSchedule : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public bool Enabled { get; set; }

    // Giờ đi ngủ (giờ Việt Nam); null = không đặt.
    public TimeOnly? BedtimeWeekday { get; set; }
    public TimeOnly? BedtimeWeekend { get; set; }

    // Nghỉ mắt bắt buộc và loại lời nhắc hiện trên màn nghỉ.
    public bool BreakEnabled { get; set; }
    public int BreakAfterMinutes { get; set; } = 20;
    public int BreakDurationMinutes { get; set; } = 5;
    public bool BreakRemindEyes { get; set; } = true;
    public bool BreakRemindWater { get; set; } = true;
    public bool BreakRemindMove { get; set; } = true;

    // Giới hạn thời gian nhìn màn hình mỗi ngày.
    public bool ScreenLimitEnabled { get; set; }
    public int ScreenLimitWeekdayMinutes { get; set; } = 60;
    public int ScreenLimitWeekendMinutes { get; set; } = 120;

    // Trạng thái lúc chạy do backend ghi (UTC); xóa mỗi lần phụ huynh lưu cấu hình.
    public DateTime? ContinuousReadingSince { get; set; }
    public DateTime? LastReadingSeenAt { get; set; }
    public DateTime? BreakUntil { get; set; }

    // Thời gian dùng app trong ngày Việt Nam UsageDate; KHÔNG xóa khi phụ huynh lưu cấu hình.
    public DateOnly? UsageDate { get; set; }
    public int UsageSeconds { get; set; }
}
