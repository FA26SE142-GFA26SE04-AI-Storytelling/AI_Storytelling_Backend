using System.Globalization;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.ReadingTime;

public enum ReadingTimeState
{
    Allowed,
    EndingSoon,
    Grace,
    Outside,
    Bedtime,
    ScreenLimitReached,
    Break
}

/// <summary>Mốc kết thúc gần nhất khiến bé phải dừng.</summary>
public enum ReadingTimeEndReason
{
    Window,
    Bedtime,
    ScreenLimit
}

/// <summary>Lời nhắc trên màn nghỉ mắt.</summary>
public enum BreakActivity
{
    Eyes,
    Water,
    Move
}

/// <summary>Trạng thái "Thời gian &amp; thói quen" tại một thời điểm. Mọi mốc là UTC.</summary>
public sealed record ReadingTimeStatus
{
    public static readonly ReadingTimeStatus Free = new() { State = ReadingTimeState.Allowed };

    public ReadingTimeState State { get; init; }
    public ReadingTimeEndReason? EndReason { get; init; }
    public DateTime? EndsAt { get; init; }
    public DateTime? GraceEndsAt { get; init; }
    public DateTime? BreakUntil { get; init; }
    public BreakActivity? BreakActivity { get; init; }
    public DateTime? NextAllowedAt { get; init; }
    /// <summary>Giờ ngủ đang áp dụng (khi Bedtime) hoặc sắp tới trong hôm nay.</summary>
    public DateTime? BedtimeAt { get; init; }
    public int? ScreenMinutesUsed { get; init; }
    public int? ScreenMinutesLimit { get; init; }
}

public readonly record struct ScheduleWindowInput(ScheduleDayType DayType, TimeOnly Start, TimeOnly End);

/// <summary>Khoảng giờ yên lặng (giờ ngủ): app của bé không gửi thông báo trong khoảng này. Mốc là UTC.</summary>
public readonly record struct QuietPeriod(DateTime FromUtc, DateTime ToUtc);

public sealed record ScheduleSettingsInput(
    bool BreakEnabled,
    int BreakAfterMinutes,
    int BreakDurationMinutes,
    bool RemindEyes,
    bool RemindWater,
    bool RemindMove,
    int ScreenLimitWeekdayMinutes,
    int ScreenLimitWeekendMinutes,
    TimeOnly? BedtimeWeekday,
    TimeOnly? BedtimeWeekend,
    IReadOnlyCollection<ScheduleWindowInput> Windows,
    TimeOnly? ReminderWeekday = null,
    TimeOnly? ReminderWeekend = null,
    int? WeeklyGoalDays = null);

/// <summary>
/// Luật "Thời gian &amp; thói quen" của hồ sơ trẻ (giờ Việt Nam): khung giờ, giờ ngủ, giới hạn màn hình,
/// nghỉ mắt bắt buộc; báo trước 5 phút, gia hạn 10 phút cho truyện đang đọc. Không truy cập DB.
/// </summary>
public static class ReadingSchedulePolicy
{
    // Một điều kiện chặn đang có hiệu lực: trạng thái chặn, lý do, mốc bắt đầu chặn, hạn gia hạn (null = không có gia hạn).
    private sealed record ActiveBlock(
        ReadingTimeState State, ReadingTimeEndReason EndReason, DateTime? EndsAt, DateTime? GraceEndsAt,
        DateTime? NextAllowedAt);

    public static readonly TimeSpan WarningLead = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MinimumRestGap = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan StateWriteInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxCountedGap = TimeSpan.FromMinutes(2);
    public static readonly TimeOnly EarliestBedtime = new(18, 0);
    public static readonly TimeOnly LatestBedtime = new(23, 30);
    public static readonly TimeOnly DefaultWakeTime = new(6, 0);

    public const int MaxWindowsPerDayType = 2;
    public const int MinBreakAfterMinutes = 15;
    public const int MaxBreakAfterMinutes = 60;
    public const int MinBreakDurationMinutes = 1;
    public const int MaxBreakDurationMinutes = 15;
    public const int MinScreenLimitMinutes = 10;
    public const int MaxScreenLimitMinutes = 240;
    public const int MinWeeklyGoalDays = 1;
    public const int MaxWeeklyGoalDays = 7;
    public const int ReminderHorizonDays = 7;

    public const string BreakErrorCode = "READING_TIME_BREAK";
    public const string BedtimeErrorCode = "READING_TIME_BEDTIME";
    public const string ScreenLimitErrorCode = "READING_TIME_SCREEN_LIMIT";
    public const string OutsideErrorCode = "READING_TIME_OUTSIDE";

    public const string ChildOutsideMessage = "Giờ đọc hôm nay hết rồi, hẹn bé lần sau nhé!";
    public const string ChildBedtimeMessage = "Đến giờ đi ngủ rồi, chúc bé ngủ ngon!";
    public const string ChildScreenLimitMessage = "Hôm nay bé dùng đủ thời gian rồi, mai mình đọc tiếp nhé!";
    public const string ChildTimeUpMessage = "Hết giờ đọc rồi, hẹn bé lần sau nhé!";
    public const string EndingSoonMessage = "Sắp đến giờ nghỉ rồi, bé đọc nốt nhé!";
    public const string GraceMessage = "Đến giờ nghỉ rồi, bé đọc nốt đoạn này nhé!";

    public const string InvalidTimeMessage = "Giờ không hợp lệ, hãy dùng dạng HH:mm.";
    public const string InvalidDayTypeMessage = "Loại ngày không hợp lệ.";
    public const string StartBeforeEndMessage = "Giờ bắt đầu phải trước giờ kết thúc.";
    public const string MaxWindowsMessage = "Mỗi loại ngày tối đa 2 khung giờ.";
    public const string BreakAfterMessage = "Nghỉ sau mỗi 15–60 phút đọc.";
    public const string BreakDurationMessage = "Thời gian nghỉ từ 1 đến 15 phút.";
    public const string BreakActivityRequiredMessage = "Hãy chọn ít nhất một lời nhắc khi nghỉ (mắt, uống nước, vận động).";
    public const string ScreenLimitMessage = "Giới hạn màn hình từ 10 đến 240 phút mỗi ngày.";
    public const string BedtimeRangeMessage = "Giờ đi ngủ phải từ 18:00 đến 23:30.";
    public const string ReminderEarliestMessage = "Giờ nhắc đọc phải từ 06:00.";
    public const string ReminderBeforeBedtimeMessage = "Giờ nhắc đọc phải trước giờ đi ngủ.";
    public const string ReminderInWindowMessage = "Giờ nhắc đọc phải nằm trong khung giờ được dùng.";
    public const string WeeklyGoalMessage = "Mục tiêu từ 1 đến 7 ngày mỗi tuần.";
    public const string ReadingReminderMessage = "Đến giờ đọc truyện rồi, bé vào đọc nhé!";

    public static string OverlapMessage(ScheduleDayType dayType) =>
        $"Hai khung giờ {(dayType == ScheduleDayType.Weekday ? "ngày thường" : "cuối tuần")} đang chồng nhau.";

    public static ScheduleDayType DayTypeOf(DateTime localDate) =>
        localDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? ScheduleDayType.Weekend
            : ScheduleDayType.Weekday;

    /// <param name="readingStartedAtUtc">Lúc phiên đọc đang mở bắt đầu; có giá trị thì được xét gia hạn.</param>
    public static ReadingTimeStatus Evaluate(
        ReadingSchedule? schedule, IReadOnlyCollection<ReadingScheduleWindow> windows,
        DateTime nowUtc, DateTime? readingStartedAtUtc = null)
    {
        if (schedule is not { Enabled: true })
        {
            return ReadingTimeStatus.Free;
        }

        var today = VietnamTime.LocalDate(nowUtc);
        var limitSeconds = ScreenLimitSeconds(schedule, today);
        var usedSeconds = UsedSecondsOn(schedule, today);
        var night = BedtimePeriodAt(schedule, windows, nowUtc);
        var status = new ReadingTimeStatus
        {
            State = ReadingTimeState.Allowed,
            BedtimeAt = night?.StartUtc ?? UpcomingBedtime(schedule, today, nowUtc),
            ScreenMinutesUsed = limitSeconds == null ? null : usedSeconds / 60,
            ScreenMinutesLimit = limitSeconds / 60
        };

        // 1. Nghỉ mắt
        if (schedule.BreakEnabled && schedule.BreakUntil is { } breakUntil && nowUtc < breakUntil)
        {
            return status with
            {
                State = ReadingTimeState.Break, BreakUntil = breakUntil, BreakActivity = PickBreakActivity(schedule)
            };
        }

        // 2-4. Giờ ngủ, hết phút màn hình, ngoài khung: xét cùng lúc, mỗi điều kiện kèm hạn gia hạn riêng.
        var blocks = new List<ActiveBlock>();
        if (night is { } period)
        {
            blocks.Add(new ActiveBlock(
                ReadingTimeState.Bedtime, ReadingTimeEndReason.Bedtime, period.StartUtc,
                readingStartedAtUtc < period.StartUtc ? period.StartUtc + GracePeriod : null, period.EndUtc));
        }

        if (limitSeconds is { } limit && usedSeconds >= limit)
        {
            blocks.Add(new ActiveBlock(
                ReadingTimeState.ScreenLimitReached, ReadingTimeEndReason.ScreenLimit,
                nowUtc.AddSeconds(limit - usedSeconds),
                readingStartedAtUtc != null ? nowUtc.AddSeconds(limit + (int)GracePeriod.TotalSeconds - usedSeconds) : null,
                null));
        }

        (DateTime StartUtc, DateTime EndUtc)? window = null;
        // Loại ngày không có khung nào thì cả ngày đó không bị chặn theo khung (quyết định 24/11/2026).
        if (windows.Any(value => value.DayType == DayTypeOf(today)))
        {
            window = FindInterval(windows, nowUtc);
            if (window == null)
            {
                var lastEnd = LatestEndAtOrBefore(windows, nowUtc);
                blocks.Add(new ActiveBlock(
                    ReadingTimeState.Outside, ReadingTimeEndReason.Window, lastEnd,
                    readingStartedAtUtc < lastEnd ? lastEnd + GracePeriod : null,
                    NextWindowStart(windows, nowUtc)));
            }
        }

        if (blocks.Count > 0)
        {
            // Grace chỉ khi mọi điều kiện chặn còn trong gia hạn của chính nó; lấy theo hạn sớm nhất (tối đa 10 phút).
            if (blocks.All(block => block.GraceEndsAt > nowUtc))
            {
                var earliest = blocks.MinBy(block => block.GraceEndsAt)!;
                return status with
                {
                    State = ReadingTimeState.Grace, EndReason = earliest.EndReason,
                    EndsAt = earliest.EndsAt, GraceEndsAt = earliest.GraceEndsAt
                };
            }

            var top = blocks[0];
            return top.State switch
            {
                ReadingTimeState.ScreenLimitReached => status with { State = top.State },
                _ => status with { State = top.State, NextAllowedAt = top.NextAllowedAt }
            };
        }

        // 5. Được dùng: mốc kết thúc sớm nhất
        var ends = new List<(DateTime At, ReadingTimeEndReason Reason)>();
        if (window is { } current)
        {
            ends.Add((current.EndUtc, ReadingTimeEndReason.Window));
        }

        if (status.BedtimeAt is { } bedtime)
        {
            ends.Add((bedtime, ReadingTimeEndReason.Bedtime));
        }

        if (limitSeconds is { } dailyLimit)
        {
            ends.Add((nowUtc.AddSeconds(dailyLimit - usedSeconds), ReadingTimeEndReason.ScreenLimit));
        }

        if (ends.Count == 0)
        {
            return status;
        }

        var first = ends.MinBy(end => end.At);
        return status with
        {
            State = first.At - nowUtc <= WarningLead ? ReadingTimeState.EndingSoon : ReadingTimeState.Allowed,
            EndReason = first.Reason,
            EndsAt = first.At
        };
    }

    /// <summary>Cập nhật bộ đếm đọc liên tục khi app gọi signal. Trả true nếu có thay đổi cần lưu.</summary>
    public static bool TrackReading(ReadingSchedule schedule, DateTime nowUtc)
    {
        if (!schedule.Enabled || !schedule.BreakEnabled)
        {
            return false;
        }

        if (schedule.BreakUntil is { } breakUntil)
        {
            if (nowUtc < breakUntil)
            {
                return false;
            }

            schedule.BreakUntil = null;
            schedule.ContinuousReadingSince = nowUtc;
            schedule.LastReadingSeenAt = nowUtc;
            return true;
        }

        var restGap = TimeSpan.FromMinutes(Math.Max(schedule.BreakDurationMinutes, MinimumRestGap.TotalMinutes));
        if (schedule.ContinuousReadingSince == null
            || schedule.LastReadingSeenAt == null
            || nowUtc - schedule.LastReadingSeenAt.Value >= restGap)
        {
            schedule.ContinuousReadingSince = nowUtc;
            schedule.LastReadingSeenAt = nowUtc;
            return true;
        }

        if (nowUtc - schedule.ContinuousReadingSince.Value >= TimeSpan.FromMinutes(schedule.BreakAfterMinutes))
        {
            schedule.BreakUntil = nowUtc.AddMinutes(schedule.BreakDurationMinutes);
            schedule.ContinuousReadingSince = null;
            schedule.LastReadingSeenAt = nowUtc;
            return true;
        }

        if (nowUtc - schedule.LastReadingSeenAt.Value >= StateWriteInterval)
        {
            schedule.LastReadingSeenAt = nowUtc;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Cộng thời gian dùng app khi middleware ghi hoạt động của phiên bé. Khoảng cách &gt; 2 phút coi như bé đã rời app.
    /// Trả true nếu có thay đổi cần lưu.
    /// </summary>
    public static bool AddScreenTime(ReadingSchedule schedule, DateTime previousActivityUtc, DateTime nowUtc)
    {
        var today = DateOnly.FromDateTime(VietnamTime.LocalDate(nowUtc));
        var changed = false;
        if (schedule.UsageDate != today)
        {
            schedule.UsageDate = today;
            schedule.UsageSeconds = 0;
            changed = true;
        }

        var gap = nowUtc - previousActivityUtc;
        if (gap > TimeSpan.Zero && gap <= MaxCountedGap)
        {
            schedule.UsageSeconds += (int)gap.TotalSeconds;
            changed = true;
        }

        return changed;
    }

    /// <summary>Câu lỗi đầu tiên, hoặc null khi hợp lệ.</summary>
    public static string? Validate(ScheduleSettingsInput input)
    {
        if (input.BreakAfterMinutes is < MinBreakAfterMinutes or > MaxBreakAfterMinutes)
        {
            return BreakAfterMessage;
        }

        if (input.BreakDurationMinutes is < MinBreakDurationMinutes or > MaxBreakDurationMinutes)
        {
            return BreakDurationMessage;
        }

        if (input.BreakEnabled && !input.RemindEyes && !input.RemindWater && !input.RemindMove)
        {
            return BreakActivityRequiredMessage;
        }

        if (input.ScreenLimitWeekdayMinutes is < MinScreenLimitMinutes or > MaxScreenLimitMinutes
            || input.ScreenLimitWeekendMinutes is < MinScreenLimitMinutes or > MaxScreenLimitMinutes)
        {
            return ScreenLimitMessage;
        }

        foreach (var bedtime in new[] { input.BedtimeWeekday, input.BedtimeWeekend })
        {
            if (bedtime is { } value && (value < EarliestBedtime || value > LatestBedtime))
            {
                return BedtimeRangeMessage;
            }
        }

        foreach (var window in input.Windows)
        {
            if (!Enum.IsDefined(window.DayType))
            {
                return InvalidDayTypeMessage;
            }

            if (window.Start >= window.End)
            {
                return StartBeforeEndMessage;
            }
        }

        foreach (var group in input.Windows.GroupBy(window => window.DayType))
        {
            var ordered = group.OrderBy(window => window.Start).ToList();
            if (ordered.Count > MaxWindowsPerDayType)
            {
                return MaxWindowsMessage;
            }

            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].Start < ordered[i - 1].End)
                {
                    return OverlapMessage(group.Key);
                }
            }
        }

        if (input.WeeklyGoalDays is { } goal && (goal < MinWeeklyGoalDays || goal > MaxWeeklyGoalDays))
        {
            return WeeklyGoalMessage;
        }

        var reminders = new[]
        {
            (DayType: ScheduleDayType.Weekday, Reminder: input.ReminderWeekday, Bedtime: input.BedtimeWeekday),
            (DayType: ScheduleDayType.Weekend, Reminder: input.ReminderWeekend, Bedtime: input.BedtimeWeekend)
        };
        foreach (var (dayType, reminder, bedtime) in reminders)
        {
            if (reminder is not { } time)
            {
                continue;
            }

            if (time < DefaultWakeTime)
            {
                return ReminderEarliestMessage;
            }

            if (bedtime is { } bedtimeValue && time >= bedtimeValue)
            {
                return ReminderBeforeBedtimeMessage;
            }

            var dayWindows = input.Windows.Where(window => window.DayType == dayType).ToList();
            if (dayWindows.Count > 0 && !dayWindows.Any(window => time >= window.Start && time < window.End))
            {
                return ReminderInWindowMessage;
            }
        }

        return null;
    }

    /// <summary>
    /// Các thời điểm nhắc đọc trong 7 ngày kể từ hôm nay (giờ Việt Nam). Bỏ thời điểm đã qua và, nếu hôm nay bé
    /// đã hoàn thành truyện, bỏ lần nhắc hôm nay. Rỗng khi chưa bật thói quen.
    /// </summary>
    public static IReadOnlyList<DateTime> BuildReminders(ReadingSchedule schedule, DateTime nowUtc, bool readToday)
    {
        var reminders = new List<DateTime>();
        if (!schedule.HabitEnabled)
        {
            return reminders;
        }

        var today = VietnamTime.LocalDate(nowUtc);
        for (var offset = 0; offset < ReminderHorizonDays; offset++)
        {
            var day = today.AddDays(offset);
            var reminder = DayTypeOf(day) == ScheduleDayType.Weekday
                ? schedule.ReadingReminderWeekday
                : schedule.ReadingReminderWeekend;
            if (reminder is not { } time)
            {
                continue;
            }

            var at = ToUtc(day, time);
            if (at <= nowUtc || (offset == 0 && readToday))
            {
                continue;
            }

            reminders.Add(at);
        }

        return reminders;
    }

    /// <summary>
    /// Các khoảng giờ ngủ (từ giờ ngủ ngày D đến giờ thức ngày D+1) chưa kết thúc, D từ hôm qua đến hôm nay + 6.
    /// Rỗng khi chưa bật thói quen.
    /// </summary>
    public static IReadOnlyList<QuietPeriod> BuildQuietPeriods(
        ReadingSchedule schedule, IReadOnlyCollection<ReadingScheduleWindow> windows, DateTime nowUtc)
    {
        var periods = new List<QuietPeriod>();
        if (!schedule.HabitEnabled)
        {
            return periods;
        }

        var today = VietnamTime.LocalDate(nowUtc);
        for (var offset = -1; offset < ReminderHorizonDays; offset++)
        {
            var day = today.AddDays(offset);
            if (BedtimeOf(schedule, day) is not { } bedtime)
            {
                continue;
            }

            var end = WakeUtc(windows, day.AddDays(1));
            if (end > nowUtc)
            {
                periods.Add(new QuietPeriod(ToUtc(day, bedtime), end));
            }
        }

        return periods;
    }

    /// <summary>Lời nhắc của lượt nghỉ: xoay vòng trong các loại đã bật, cố định trong cùng một lượt nghỉ.</summary>
    public static BreakActivity? PickBreakActivity(ReadingSchedule schedule)
    {
        var options = new List<BreakActivity>();
        if (schedule.BreakRemindEyes)
        {
            options.Add(BreakActivity.Eyes);
        }

        if (schedule.BreakRemindWater)
        {
            options.Add(BreakActivity.Water);
        }

        if (schedule.BreakRemindMove)
        {
            options.Add(BreakActivity.Move);
        }

        if (options.Count == 0 || schedule.BreakUntil is not { } breakUntil)
        {
            return null;
        }

        return options[(int)(breakUntil.Ticks / TimeSpan.TicksPerMinute % options.Count)];
    }

    /// <summary>Trạng thái làm phiên đọc đang mở phải kết thúc (sau khi hết gia hạn).</summary>
    public static bool EndsSession(string? state) =>
        state is nameof(ReadingTimeState.Outside)
            or nameof(ReadingTimeState.Bedtime)
            or nameof(ReadingTimeState.ScreenLimitReached);

    public static string? MessageFor(ReadingTimeStatus status, DateTime nowUtc) => status.State switch
    {
        ReadingTimeState.EndingSoon => EndingSoonMessage,
        ReadingTimeState.Grace => GraceMessage,
        ReadingTimeState.Outside => ChildOutsideMessage,
        ReadingTimeState.Bedtime => ChildBedtimeMessage,
        ReadingTimeState.ScreenLimitReached => ChildScreenLimitMessage,
        ReadingTimeState.Break => status.BreakActivity is { } activity
            ? $"{BreakMessage(status.BreakUntil ?? nowUtc, nowUtc)} {BreakActivityMessage(activity)}"
            : BreakMessage(status.BreakUntil ?? nowUtc, nowUtc),
        _ => null
    };

    public static string BreakMessage(DateTime breakUntilUtc, DateTime nowUtc)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((breakUntilUtc - nowUtc).TotalMinutes));
        return $"Bé nghỉ mắt một chút nhé, còn {minutes} phút.";
    }

    public static string BreakActivityMessage(BreakActivity activity) => activity switch
    {
        BreakActivity.Eyes => "Nhìn ra xa một chút cho mắt nghỉ nhé!",
        BreakActivity.Water => "Bé uống một ngụm nước nhé!",
        _ => "Bé đứng dậy vươn vai, nhảy nhẹ vài cái nhé!"
    };

    public static string ParentOutsideMessage(DateTime? nextWindowStartUtc)
    {
        if (nextWindowStartUtc is not { } next)
        {
            return "Ngoài khung giờ đọc của bé.";
        }

        var local = next + VietnamTime.Offset;
        var day = local.DayOfWeek == DayOfWeek.Sunday ? "Chủ nhật" : $"Thứ {(int)local.DayOfWeek + 1}";
        return $"Ngoài khung giờ đọc của bé (khung kế tiếp: {day} {LocalTime(next)}).";
    }

    public static string ParentBedtimeMessage(DateTime? bedtimeUtc) =>
        bedtimeUtc is { } bedtime ? $"Đã qua giờ đi ngủ của bé ({LocalTime(bedtime)})." : "Đã qua giờ đi ngủ của bé.";

    public static string ParentScreenLimitMessage(int? limitMinutes) => $"Bé đã dùng hết {limitMinutes} phút hôm nay.";

    private static string LocalTime(DateTime utc) =>
        (utc + VietnamTime.Offset).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static int? ScreenLimitSeconds(ReadingSchedule schedule, DateTime localDate) =>
        !schedule.ScreenLimitEnabled
            ? null
            : (DayTypeOf(localDate) == ScheduleDayType.Weekday
                ? schedule.ScreenLimitWeekdayMinutes
                : schedule.ScreenLimitWeekendMinutes) * 60;

    private static int UsedSecondsOn(ReadingSchedule schedule, DateTime localDate) =>
        schedule.UsageDate == DateOnly.FromDateTime(localDate) ? schedule.UsageSeconds : 0;

    private static TimeOnly? BedtimeOf(ReadingSchedule schedule, DateTime localDate) =>
        DayTypeOf(localDate) == ScheduleDayType.Weekday ? schedule.BedtimeWeekday : schedule.BedtimeWeekend;

    private static DateTime? UpcomingBedtime(ReadingSchedule schedule, DateTime today, DateTime nowUtc) =>
        BedtimeOf(schedule, today) is { } bedtime && ToUtc(today, bedtime) > nowUtc ? ToUtc(today, bedtime) : null;

    // Giai đoạn ngủ chứa nowUtc: từ giờ ngủ của ngày D tới giờ thức của D+1 (xét D = hôm qua và hôm nay).
    private static (DateTime StartUtc, DateTime EndUtc)? BedtimePeriodAt(
        ReadingSchedule schedule, IReadOnlyCollection<ReadingScheduleWindow> windows, DateTime nowUtc)
    {
        var today = VietnamTime.LocalDate(nowUtc);
        foreach (var day in new[] { today.AddDays(-1), today })
        {
            if (BedtimeOf(schedule, day) is not { } bedtime)
            {
                continue;
            }

            var start = ToUtc(day, bedtime);
            var end = WakeUtc(windows, day.AddDays(1));
            if (nowUtc >= start && nowUtc < end)
            {
                return (start, end);
            }
        }

        return null;
    }

    private static DateTime WakeUtc(IReadOnlyCollection<ReadingScheduleWindow> windows, DateTime localDate) =>
        IntervalsOn(windows, localDate).Select(interval => (DateTime?)interval.StartUtc).FirstOrDefault()
        ?? ToUtc(localDate, DefaultWakeTime);

    // Khung của một ngày (giờ Việt Nam) đổi sang UTC, xếp theo giờ bắt đầu; khung chạm nhau được gộp.
    private static IEnumerable<(DateTime StartUtc, DateTime EndUtc)> IntervalsOn(
        IReadOnlyCollection<ReadingScheduleWindow> windows, DateTime localDate)
    {
        var dayType = DayTypeOf(localDate);
        (DateTime StartUtc, DateTime EndUtc)? current = null;
        foreach (var window in windows.Where(value => value.DayType == dayType).OrderBy(value => value.StartTime))
        {
            var start = ToUtc(localDate, window.StartTime);
            var end = ToUtc(localDate, window.EndTime);
            if (current is { } open && start <= open.EndUtc)
            {
                current = (open.StartUtc, end > open.EndUtc ? end : open.EndUtc);
                continue;
            }

            if (current is { } done)
            {
                yield return done;
            }

            current = (start, end);
        }

        if (current is { } last)
        {
            yield return last;
        }
    }

    private static DateTime ToUtc(DateTime localDate, TimeOnly time) =>
        DateTime.SpecifyKind(localDate.Date + time.ToTimeSpan() - VietnamTime.Offset, DateTimeKind.Utc);

    private static (DateTime StartUtc, DateTime EndUtc)? FindInterval(
        IReadOnlyCollection<ReadingScheduleWindow> windows, DateTime instantUtc)
    {
        foreach (var interval in IntervalsOn(windows, VietnamTime.LocalDate(instantUtc)))
        {
            if (instantUtc >= interval.StartUtc && instantUtc < interval.EndUtc)
            {
                return interval;
            }
        }

        return null;
    }

    private static DateTime? LatestEndAtOrBefore(IReadOnlyCollection<ReadingScheduleWindow> windows, DateTime instantUtc)
    {
        var today = VietnamTime.LocalDate(instantUtc);
        return IntervalsOn(windows, today.AddDays(-1))
            .Concat(IntervalsOn(windows, today))
            .Select(interval => interval.EndUtc)
            .Where(end => end <= instantUtc)
            .Select(end => (DateTime?)end)
            .Max();
    }

    private static DateTime? NextWindowStart(IReadOnlyCollection<ReadingScheduleWindow> windows, DateTime nowUtc)
    {
        var today = VietnamTime.LocalDate(nowUtc);
        for (var offset = 0; offset <= 7; offset++)
        {
            foreach (var interval in IntervalsOn(windows, today.AddDays(offset)))
            {
                if (interval.StartUtc > nowUtc)
                {
                    return interval.StartUtc;
                }
            }
        }

        return null;
    }
}
