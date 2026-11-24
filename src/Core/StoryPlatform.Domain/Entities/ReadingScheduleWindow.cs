using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>Một khung giờ được dùng app (giờ Việt Nam, không vắt qua nửa đêm). Tối đa 2 khung mỗi loại ngày.</summary>
public class ReadingScheduleWindow : BaseEntity
{
    public int ReadingScheduleId { get; set; }
    public virtual ReadingSchedule? ReadingSchedule { get; set; }

    public ScheduleDayType DayType { get; set; }
    public int Slot { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}
