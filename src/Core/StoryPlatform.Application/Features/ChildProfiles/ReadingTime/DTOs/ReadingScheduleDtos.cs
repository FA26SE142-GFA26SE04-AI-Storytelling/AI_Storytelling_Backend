using System.Text.Json.Serialization;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.ReadingTime.DTOs;

public class ReadingScheduleDto
{
    public int ChildProfileId { get; set; }
    public bool Enabled { get; set; }
    /// <summary>Giờ đi ngủ dạng HH:mm (giờ Việt Nam); null = không đặt.</summary>
    public string? BedtimeWeekday { get; set; }
    public string? BedtimeWeekend { get; set; }
    public bool BreakEnabled { get; set; }
    public int BreakAfterMinutes { get; set; }
    public int BreakDurationMinutes { get; set; }
    public bool BreakRemindEyes { get; set; }
    public bool BreakRemindWater { get; set; }
    public bool BreakRemindMove { get; set; }
    public bool ScreenLimitEnabled { get; set; }
    public int ScreenLimitWeekdayMinutes { get; set; }
    public int ScreenLimitWeekendMinutes { get; set; }
    public bool HabitEnabled { get; set; }
    /// <summary>Giờ nhắc đọc dạng HH:mm (giờ Việt Nam); null = không nhắc loại ngày đó.</summary>
    public string? ReadingReminderWeekday { get; set; }
    public string? ReadingReminderWeekend { get; set; }
    /// <summary>Mục tiêu số ngày đọc mỗi tuần (1–7); null = không đặt.</summary>
    public int? WeeklyGoalDays { get; set; }
    /// <summary>Tiến độ tuần và chuỗi ngày đọc; null khi chưa lưu cấu hình lần nào.</summary>
    public ReadingHabitProgressDto? Habit { get; set; }
    public List<ReadingScheduleWindowDto> Windows { get; set; } = new();
    /// <summary>true khi chưa lưu lần nào: đây là gợi ý theo độ tuổi để form điền sẵn.</summary>
    public bool IsSuggested { get; set; }
}

public class ReadingScheduleWindowDto
{
    public string DayType { get; set; } = string.Empty;
    public int Slot { get; set; }
    /// <summary>Giờ Việt Nam dạng HH:mm.</summary>
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
}

public class SetReadingScheduleRequestDto
{
    public bool Enabled { get; set; }
    /// <summary>HH:mm từ 18:00 đến 23:30; null hoặc rỗng = không đặt.</summary>
    public string? BedtimeWeekday { get; set; }
    public string? BedtimeWeekend { get; set; }
    public bool BreakEnabled { get; set; }
    public int BreakAfterMinutes { get; set; } = 20;
    public int BreakDurationMinutes { get; set; } = 5;
    public bool BreakRemindEyes { get; set; } = true;
    public bool BreakRemindWater { get; set; } = true;
    public bool BreakRemindMove { get; set; } = true;
    public bool ScreenLimitEnabled { get; set; }
    public int ScreenLimitWeekdayMinutes { get; set; } = 60;
    public int ScreenLimitWeekendMinutes { get; set; } = 120;
    public bool HabitEnabled { get; set; }
    /// <summary>HH:mm từ 06:00, trước giờ ngủ, trong khung giờ được dùng; null hoặc rỗng = không nhắc.</summary>
    public string? ReadingReminderWeekday { get; set; }
    public string? ReadingReminderWeekend { get; set; }
    /// <summary>1–7; null = không đặt mục tiêu.</summary>
    public int? WeeklyGoalDays { get; set; }
    public List<SetReadingScheduleWindowRequestDto>? Windows { get; set; } = new();
}

public class SetReadingScheduleWindowRequestDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ScheduleDayType DayType { get; set; }

    /// <summary>Giờ Việt Nam dạng HH:mm.</summary>
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
}

/// <summary>Trạng thái "Thời gian &amp; thói quen" trả cho app của bé (signal, /me).</summary>
public class ReadingTimeStatusDto
{
    /// <summary>Allowed | EndingSoon | Grace | Outside | Bedtime | ScreenLimitReached | Break.</summary>
    public string State { get; set; } = string.Empty;
    /// <summary>Window | Bedtime | ScreenLimit — mốc kết thúc gần nhất (khi Allowed/EndingSoon/Grace).</summary>
    public string? EndReason { get; set; }
    public DateTime? EndsAt { get; set; }
    public DateTime? GraceEndsAt { get; set; }
    public DateTime? BreakUntil { get; set; }
    /// <summary>Eyes | Water | Move — lời nhắc trên màn nghỉ.</summary>
    public string? BreakActivity { get; set; }
    public DateTime? NextAllowedAt { get; set; }
    public DateTime? BedtimeAt { get; set; }
    public int? ScreenMinutesUsed { get; set; }
    public int? ScreenMinutesLimit { get; set; }
    public string? Message { get; set; }
}

/// <summary>Tiến độ thói quen đọc hiển thị cho phụ huynh.</summary>
public class ReadingHabitProgressDto
{
    /// <summary>Chuỗi ngày đọc còn hiệu lực (0 nếu bé đã bỏ lỡ từ hôm kia trở đi).</summary>
    public int StreakDays { get; set; }
    /// <summary>Số ngày trong tuần này (từ Thứ 2, giờ Việt Nam) bé hoàn thành ≥ 1 truyện.</summary>
    public int DaysReadThisWeek { get; set; }
    public bool GoalMetThisWeek { get; set; }
}

/// <summary>Khối thói quen đọc trong /me của bé: tiến độ, lịch nhắc cho app đặt thông báo cục bộ, giờ yên lặng.</summary>
public class ChildHabitDto
{
    /// <summary>false khi phụ huynh chưa bật thói quen: reminders và quietPeriods rỗng.</summary>
    public bool Enabled { get; set; }
    public int StreakDays { get; set; }
    public int DaysReadThisWeek { get; set; }
    public int? WeeklyGoalDays { get; set; }
    public bool GoalMetThisWeek { get; set; }
    public List<HabitReminderDto> Reminders { get; set; } = new();
    public List<HabitQuietPeriodDto> QuietPeriods { get; set; } = new();
}

public class HabitReminderDto
{
    /// <summary>Thời điểm nhắc (UTC).</summary>
    public DateTime At { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>Khoảng giờ ngủ (UTC): app không gửi thông báo nào trong khoảng này.</summary>
public class HabitQuietPeriodDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}
