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
