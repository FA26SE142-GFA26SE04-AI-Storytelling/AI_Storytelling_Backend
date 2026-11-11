namespace StoryPlatform.Application.Common.Time;

/// <summary>
/// Mốc ngày / tuần / tháng theo giờ Việt Nam (UTC+7). Mọi tham số và kết quả là DateTime UTC.
/// </summary>
public static class VietnamTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    /// <summary>Ngày lịch Việt Nam của một thời điểm UTC.</summary>
    public static DateTime LocalDate(DateTime utc) => (utc + Offset).Date;

    public static DateTime StartOfDayUtc(DateTime utcNow) =>
        DateTime.SpecifyKind(LocalDate(utcNow) - Offset, DateTimeKind.Utc);

    /// <summary>00:00 thứ Hai của tuần hiện tại theo giờ Việt Nam.</summary>
    public static DateTime StartOfWeekUtc(DateTime utcNow)
    {
        var local = LocalDate(utcNow);
        var daysSinceMonday = ((int)local.DayOfWeek + 6) % 7;
        return DateTime.SpecifyKind(local.AddDays(-daysSinceMonday) - Offset, DateTimeKind.Utc);
    }

    public static DateTime StartOfNextWeekUtc(DateTime utcNow) => StartOfWeekUtc(utcNow).AddDays(7);

    public static DateTime StartOfMonthUtc(DateTime utcNow)
    {
        var local = LocalDate(utcNow);
        return DateTime.SpecifyKind(new DateTime(local.Year, local.Month, 1) - Offset, DateTimeKind.Utc);
    }

    public static DateTime StartOfNextMonthUtc(DateTime utcNow)
    {
        var local = LocalDate(utcNow);
        return DateTime.SpecifyKind(new DateTime(local.Year, local.Month, 1).AddMonths(1) - Offset, DateTimeKind.Utc);
    }
}
