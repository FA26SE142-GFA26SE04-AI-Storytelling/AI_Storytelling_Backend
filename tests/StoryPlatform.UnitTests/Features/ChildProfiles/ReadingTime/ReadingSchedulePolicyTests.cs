using System.Globalization;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Features.ChildProfiles.ReadingTime;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.ReadingTime;

public sealed class ReadingSchedulePolicyTests
{
    // 23/11/2026 là Thứ Hai; 20/11 Thứ Sáu; 21–22/11 Thứ Bảy, Chủ nhật; 24/11 Thứ Ba; 28–29/11 Thứ Bảy, Chủ nhật.
    private static DateTime Vn(int day, int hour, int minute) =>
        new DateTime(2026, 11, day, hour, minute, 0, DateTimeKind.Utc).AddHours(-7);

    private static TimeOnly T(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    private static ReadingSchedule Schedule(bool breakEnabled = false) => new()
    {
        Id = 1, ChildProfileId = 1, Enabled = true, BreakEnabled = breakEnabled,
        BreakAfterMinutes = 20, BreakDurationMinutes = 5
    };

    private static ReadingSchedule WithBedtime(string weekday = "20:30", string weekend = "21:00")
    {
        var schedule = Schedule();
        schedule.BedtimeWeekday = T(weekday);
        schedule.BedtimeWeekend = T(weekend);
        return schedule;
    }

    private static ReadingSchedule WithScreenLimit(int usedMinutes, int usageDay = 23, int weekday = 45, int weekend = 90)
    {
        var schedule = Schedule();
        schedule.ScreenLimitEnabled = true;
        schedule.ScreenLimitWeekdayMinutes = weekday;
        schedule.ScreenLimitWeekendMinutes = weekend;
        schedule.UsageDate = new DateOnly(2026, 11, usageDay);
        schedule.UsageSeconds = usedMinutes * 60;
        return schedule;
    }

    private static ReadingScheduleWindow W(ScheduleDayType type, string start, string end) => new()
    {
        ReadingScheduleId = 1, DayType = type, StartTime = T(start), EndTime = T(end)
    };

    private static readonly ReadingScheduleWindow[] Default =
    [
        W(ScheduleDayType.Weekday, "07:00", "20:30"),
        W(ScheduleDayType.Weekend, "08:00", "11:00"),
        W(ScheduleDayType.Weekend, "15:00", "20:00")
    ];

    private static readonly ReadingScheduleWindow[] LongWeekday = [W(ScheduleDayType.Weekday, "07:00", "22:00")];

    private static ScheduleWindowInput In(ScheduleDayType type, string start, string end) => new(type, T(start), T(end));

    private static ScheduleSettingsInput Settings(
        IReadOnlyCollection<ScheduleWindowInput>? windows = null, bool breakEnabled = true, int breakAfter = 20,
        int breakDuration = 5, bool eyes = true, bool water = true, bool move = true, int screenWeekday = 60,
        int screenWeekend = 120, string? bedtimeWeekday = null, string? bedtimeWeekend = null) =>
        new(breakEnabled, breakAfter, breakDuration, eyes, water, move, screenWeekday, screenWeekend,
            bedtimeWeekday == null ? null : T(bedtimeWeekday), bedtimeWeekend == null ? null : T(bedtimeWeekend),
            windows ?? []);

    // ---------- Khung giờ ----------

    [Fact]
    public void Evaluate_NoSchedule_IsAllowed() =>
        Assert.Equal(ReadingTimeState.Allowed, ReadingSchedulePolicy.Evaluate(null, [], Vn(23, 3, 0)).State);

    [Fact]
    public void Evaluate_Disabled_IsAllowedAnyTime()
    {
        var schedule = WithBedtime();
        schedule.Enabled = false;

        Assert.Equal(ReadingTimeState.Allowed, ReadingSchedulePolicy.Evaluate(schedule, Default, Vn(23, 23, 0)).State);
    }

    [Fact]
    public void Evaluate_EnabledWithNothingSet_IsAllowedWithoutEnd()
    {
        var status = ReadingSchedulePolicy.Evaluate(Schedule(), [], Vn(23, 23, 0));

        Assert.Equal(ReadingTimeState.Allowed, status.State);
        Assert.Null(status.EndsAt);
        Assert.Null(status.ScreenMinutesLimit);
    }

    [Theory]
    [InlineData(6, 59, ReadingTimeState.Outside)]
    [InlineData(7, 0, ReadingTimeState.Allowed)]
    [InlineData(20, 24, ReadingTimeState.Allowed)]
    [InlineData(20, 25, ReadingTimeState.EndingSoon)]
    [InlineData(20, 30, ReadingTimeState.Outside)]
    public void Evaluate_WeekdayBoundaries(int hour, int minute, ReadingTimeState expected) =>
        Assert.Equal(expected, ReadingSchedulePolicy.Evaluate(Schedule(), Default, Vn(23, hour, minute)).State);

    [Fact]
    public void Evaluate_InsideWindow_EndsAtWindowEnd()
    {
        var status = ReadingSchedulePolicy.Evaluate(Schedule(), Default, Vn(23, 10, 0));

        Assert.Equal(Vn(23, 20, 30), status.EndsAt);
        Assert.Equal(ReadingTimeEndReason.Window, status.EndReason);
    }

    [Fact]
    public void Evaluate_BeforeFirstWindow_ReturnsNextAllowed()
    {
        var status = ReadingSchedulePolicy.Evaluate(Schedule(), Default, Vn(23, 6, 59));

        Assert.Equal(ReadingTimeState.Outside, status.State);
        Assert.Equal(Vn(23, 7, 0), status.NextAllowedAt);
    }

    [Fact]
    public void Evaluate_Weekend_UsesWeekendWindows()
    {
        Assert.Equal(ReadingTimeState.Outside, ReadingSchedulePolicy.Evaluate(Schedule(), Default, Vn(21, 7, 30)).State);
        Assert.Equal(ReadingTimeState.Allowed, ReadingSchedulePolicy.Evaluate(Schedule(), Default, Vn(23, 7, 30)).State);
    }

    [Fact]
    public void DayTypeOf_SwitchesAtVietnamMidnight()
    {
        Assert.Equal(ScheduleDayType.Weekday, ReadingSchedulePolicy.DayTypeOf(VietnamTime.LocalDate(Vn(20, 23, 59))));
        Assert.Equal(ScheduleDayType.Weekend, ReadingSchedulePolicy.DayTypeOf(VietnamTime.LocalDate(Vn(21, 0, 0))));
    }

    [Fact]
    public void Evaluate_OnlyWeekdayWindows_WeekendIsNotRestrictedByWindows()
    {
        var status = ReadingSchedulePolicy.Evaluate(
            Schedule(), [W(ScheduleDayType.Weekday, "07:00", "20:30")], Vn(21, 10, 0));

        Assert.Equal(ReadingTimeState.Allowed, status.State);
        Assert.Null(status.EndsAt);
        Assert.Null(status.NextAllowedAt);
    }

    [Fact]
    public void Evaluate_OnlyWeekdayWindows_WeekendStillHasBedtime()
    {
        ReadingScheduleWindow[] weekdayOnly = [W(ScheduleDayType.Weekday, "07:00", "20:30")];

        var before = ReadingSchedulePolicy.Evaluate(WithBedtime(), weekdayOnly, Vn(21, 20, 50));
        var after = ReadingSchedulePolicy.Evaluate(WithBedtime(), weekdayOnly, Vn(21, 21, 30));

        Assert.Equal(ReadingTimeState.Allowed, before.State);
        Assert.Equal(ReadingTimeEndReason.Bedtime, before.EndReason);
        Assert.Equal(Vn(21, 21, 0), before.EndsAt);
        Assert.Equal(ReadingTimeState.Bedtime, after.State);
        Assert.Equal(Vn(22, 6, 0), after.NextAllowedAt);
    }

    [Fact]
    public void Evaluate_OnlyWeekendWindows_WeekdayIsNotRestrictedByWindows()
    {
        var status = ReadingSchedulePolicy.Evaluate(
            Schedule(), [W(ScheduleDayType.Weekend, "08:00", "11:00")], Vn(23, 10, 0));

        Assert.Equal(ReadingTimeState.Allowed, status.State);
        Assert.Null(status.EndsAt);
    }

    [Fact]
    public void Evaluate_ReadingStartedBeforeWindowEnd_GetsTenMinutesGrace()
    {
        var started = Vn(23, 20, 0);

        var atEnd = ReadingSchedulePolicy.Evaluate(Schedule(), Default, Vn(23, 20, 30), started);
        var lastMinute = ReadingSchedulePolicy.Evaluate(Schedule(), Default, Vn(23, 20, 39), started);
        var over = ReadingSchedulePolicy.Evaluate(Schedule(), Default, Vn(23, 20, 40), started);

        Assert.Equal(ReadingTimeState.Grace, atEnd.State);
        Assert.Equal(ReadingTimeEndReason.Window, atEnd.EndReason);
        Assert.Equal(Vn(23, 20, 30), atEnd.EndsAt);
        Assert.Equal(Vn(23, 20, 40), atEnd.GraceEndsAt);
        Assert.Equal(ReadingTimeState.Grace, lastMinute.State);
        Assert.Equal(ReadingTimeState.Outside, over.State);
    }

    [Fact]
    public void Evaluate_ResumedSessionStartedDaysAgo_StillGetsGrace() =>
        Assert.Equal(ReadingTimeState.Grace,
            ReadingSchedulePolicy.Evaluate(Schedule(), Default, Vn(23, 20, 35), Vn(20, 19, 0)).State);

    [Fact]
    public void Evaluate_WithoutReadingSession_HasNoGrace() =>
        Assert.Equal(ReadingTimeState.Outside, ReadingSchedulePolicy.Evaluate(Schedule(), Default, Vn(23, 20, 35)).State);

    [Fact]
    public void Evaluate_WindowGraceCrossesMidnight()
    {
        var status = ReadingSchedulePolicy.Evaluate(
            Schedule(), [W(ScheduleDayType.Weekday, "07:00", "23:59")], Vn(24, 0, 5), Vn(23, 23, 50));

        Assert.Equal(ReadingTimeState.Grace, status.State);
        Assert.Equal(Vn(24, 0, 9), status.GraceEndsAt);
    }

    [Fact]
    public void Evaluate_TouchingWindows_AreOneWindow()
    {
        var windows = new[] { W(ScheduleDayType.Weekday, "07:00", "08:00"), W(ScheduleDayType.Weekday, "08:00", "09:00") };

        var status = ReadingSchedulePolicy.Evaluate(Schedule(), windows, Vn(23, 7, 57));

        Assert.Equal(ReadingTimeState.Allowed, status.State);
        Assert.Equal(Vn(23, 9, 0), status.EndsAt);
    }

    // ---------- Giờ ngủ ----------

    [Fact]
    public void Evaluate_BeforeBedtime_EndsAtBedtime()
    {
        var status = ReadingSchedulePolicy.Evaluate(WithBedtime(), LongWeekday, Vn(23, 19, 0));

        Assert.Equal(ReadingTimeState.Allowed, status.State);
        Assert.Equal(ReadingTimeEndReason.Bedtime, status.EndReason);
        Assert.Equal(Vn(23, 20, 30), status.EndsAt);
        Assert.Equal(Vn(23, 20, 30), status.BedtimeAt);
    }

    [Fact]
    public void Evaluate_AfterBedtime_IsBedtimeUntilFirstWindowTomorrow()
    {
        var status = ReadingSchedulePolicy.Evaluate(WithBedtime(), LongWeekday, Vn(23, 21, 0));

        Assert.Equal(ReadingTimeState.Bedtime, status.State);
        Assert.Equal(Vn(23, 20, 30), status.BedtimeAt);
        Assert.Equal(Vn(24, 7, 0), status.NextAllowedAt);
    }

    [Fact]
    public void Evaluate_AfterMidnight_StillBedtime() =>
        Assert.Equal(ReadingTimeState.Bedtime, ReadingSchedulePolicy.Evaluate(WithBedtime(), LongWeekday, Vn(24, 2, 0)).State);

    [Fact]
    public void Evaluate_BedtimeWithoutWindows_WakesAtSix()
    {
        var night = ReadingSchedulePolicy.Evaluate(WithBedtime(), [], Vn(24, 5, 59));
        var morning = ReadingSchedulePolicy.Evaluate(WithBedtime(), [], Vn(24, 6, 0));

        Assert.Equal(ReadingTimeState.Bedtime, night.State);
        Assert.Equal(Vn(24, 6, 0), night.NextAllowedAt);
        Assert.Equal(ReadingTimeState.Allowed, morning.State);
    }

    [Fact]
    public void Evaluate_ReadingAtBedtime_GetsGraceThenBedtime()
    {
        var started = Vn(23, 20, 0);

        var grace = ReadingSchedulePolicy.Evaluate(WithBedtime(), LongWeekday, Vn(23, 20, 35), started);
        var over = ReadingSchedulePolicy.Evaluate(WithBedtime(), LongWeekday, Vn(23, 20, 40), started);

        Assert.Equal(ReadingTimeState.Grace, grace.State);
        Assert.Equal(ReadingTimeEndReason.Bedtime, grace.EndReason);
        Assert.Equal(Vn(23, 20, 40), grace.GraceEndsAt);
        Assert.Equal(ReadingTimeState.Bedtime, over.State);
    }

    [Fact]
    public void Evaluate_WeekendUsesWeekendBedtime()
    {
        Assert.Equal(ReadingTimeState.Bedtime, ReadingSchedulePolicy.Evaluate(WithBedtime(), [], Vn(20, 20, 45)).State);
        var saturday = ReadingSchedulePolicy.Evaluate(WithBedtime(), [], Vn(21, 20, 45));
        Assert.Equal(ReadingTimeState.Allowed, saturday.State);
        Assert.Equal(Vn(21, 21, 0), saturday.EndsAt);
    }

    // ---------- Thời gian màn hình ----------

    [Fact]
    public void AddScreenTime_ShortGap_IsCounted()
    {
        var schedule = WithScreenLimit(0);

        Assert.True(ReadingSchedulePolicy.AddScreenTime(schedule, Vn(23, 10, 0).AddSeconds(-70), Vn(23, 10, 0)));
        Assert.Equal(70, schedule.UsageSeconds);
    }

    [Fact]
    public void AddScreenTime_GapOverTwoMinutes_IsNotCounted()
    {
        var schedule = WithScreenLimit(5);

        Assert.False(ReadingSchedulePolicy.AddScreenTime(schedule, Vn(23, 10, 0).AddMinutes(-3), Vn(23, 10, 0)));
        Assert.Equal(300, schedule.UsageSeconds);
    }

    [Fact]
    public void AddScreenTime_NewDay_ResetsCounter()
    {
        var schedule = WithScreenLimit(50, usageDay: 22);

        Assert.True(ReadingSchedulePolicy.AddScreenTime(schedule, Vn(23, 10, 0).AddSeconds(-60), Vn(23, 10, 0)));
        Assert.Equal(new DateOnly(2026, 11, 23), schedule.UsageDate);
        Assert.Equal(60, schedule.UsageSeconds);
    }

    [Fact]
    public void Evaluate_ScreenAlmostUsedUp_IsEndingSoon()
    {
        var status = ReadingSchedulePolicy.Evaluate(WithScreenLimit(41), [], Vn(23, 10, 0));

        Assert.Equal(ReadingTimeState.EndingSoon, status.State);
        Assert.Equal(ReadingTimeEndReason.ScreenLimit, status.EndReason);
        Assert.Equal(Vn(23, 10, 4), status.EndsAt);
        Assert.Equal(41, status.ScreenMinutesUsed);
        Assert.Equal(45, status.ScreenMinutesLimit);
    }

    [Fact]
    public void Evaluate_ScreenUsedUp_IsScreenLimitReached() =>
        Assert.Equal(ReadingTimeState.ScreenLimitReached,
            ReadingSchedulePolicy.Evaluate(WithScreenLimit(45), [], Vn(23, 10, 0)).State);

    [Fact]
    public void Evaluate_ScreenUsedUpWhileReading_GraceUpToTenMoreMinutes()
    {
        var grace = ReadingSchedulePolicy.Evaluate(WithScreenLimit(50), [], Vn(23, 10, 0), Vn(23, 9, 30));
        var over = ReadingSchedulePolicy.Evaluate(WithScreenLimit(55), [], Vn(23, 10, 0), Vn(23, 9, 30));

        Assert.Equal(ReadingTimeState.Grace, grace.State);
        Assert.Equal(ReadingTimeEndReason.ScreenLimit, grace.EndReason);
        Assert.Equal(Vn(23, 10, 5), grace.GraceEndsAt);
        Assert.Equal(ReadingTimeState.ScreenLimitReached, over.State);
    }

    [Fact]
    public void Evaluate_UsageFromYesterday_DoesNotCount()
    {
        var status = ReadingSchedulePolicy.Evaluate(WithScreenLimit(500, usageDay: 22), [], Vn(23, 10, 0));

        Assert.Equal(ReadingTimeState.Allowed, status.State);
        Assert.Equal(0, status.ScreenMinutesUsed);
    }

    [Fact]
    public void Evaluate_Weekend_UsesWeekendScreenLimit() =>
        Assert.Equal(ReadingTimeState.Allowed,
            ReadingSchedulePolicy.Evaluate(WithScreenLimit(60, usageDay: 21), [], Vn(21, 10, 0)).State);

    // ---------- Nghỉ mắt và thứ tự ưu tiên ----------

    [Fact]
    public void Evaluate_DuringBreak_IsBreakWithActivity()
    {
        var schedule = Schedule(breakEnabled: true);
        schedule.BreakUntil = Vn(23, 10, 3);

        var status = ReadingSchedulePolicy.Evaluate(schedule, Default, Vn(23, 10, 0));

        Assert.Equal(ReadingTimeState.Break, status.State);
        Assert.Equal(Vn(23, 10, 3), status.BreakUntil);
        Assert.NotNull(status.BreakActivity);
    }

    [Fact]
    public void Evaluate_BreakOver_IsNotBreak()
    {
        var schedule = Schedule(breakEnabled: true);
        schedule.BreakUntil = Vn(23, 10, 0);

        Assert.Equal(ReadingTimeState.Allowed, ReadingSchedulePolicy.Evaluate(schedule, Default, Vn(23, 10, 0)).State);
    }

    [Fact]
    public void Evaluate_BreakBeatsBedtime()
    {
        var schedule = WithBedtime();
        schedule.BreakEnabled = true;
        schedule.BreakUntil = Vn(23, 21, 3);

        Assert.Equal(ReadingTimeState.Break, ReadingSchedulePolicy.Evaluate(schedule, LongWeekday, Vn(23, 21, 0)).State);
    }

    [Fact]
    public void Evaluate_BedtimeBeatsScreenLimit()
    {
        var schedule = WithScreenLimit(60);
        schedule.BedtimeWeekday = T("20:30");

        Assert.Equal(ReadingTimeState.Bedtime, ReadingSchedulePolicy.Evaluate(schedule, [], Vn(23, 21, 0)).State);
    }

    [Fact]
    public void Evaluate_ScreenLimitBeatsOutside() =>
        Assert.Equal(ReadingTimeState.ScreenLimitReached,
            ReadingSchedulePolicy.Evaluate(WithScreenLimit(60), Default, Vn(23, 6, 0)).State);

    [Fact]
    public void PickBreakActivity_RotatesAmongSelectedOnly()
    {
        var schedule = Schedule(breakEnabled: true);
        schedule.BreakRemindEyes = false;
        schedule.BreakUntil = Vn(23, 10, 0);
        var first = ReadingSchedulePolicy.PickBreakActivity(schedule);
        schedule.BreakUntil = Vn(23, 10, 1);
        var second = ReadingSchedulePolicy.PickBreakActivity(schedule);

        Assert.NotEqual(first, second);
        Assert.Contains(first, new BreakActivity?[] { BreakActivity.Water, BreakActivity.Move });
        Assert.Contains(second, new BreakActivity?[] { BreakActivity.Water, BreakActivity.Move });
    }

    [Fact]
    public void PickBreakActivity_NoneSelected_IsNull()
    {
        var schedule = Schedule(breakEnabled: true);
        schedule.BreakRemindEyes = schedule.BreakRemindWater = schedule.BreakRemindMove = false;
        schedule.BreakUntil = Vn(23, 10, 0);

        Assert.Null(ReadingSchedulePolicy.PickBreakActivity(schedule));
    }

    [Theory]
    [InlineData("Outside", true)]
    [InlineData("Bedtime", true)]
    [InlineData("ScreenLimitReached", true)]
    [InlineData("Break", false)]
    [InlineData("Grace", false)]
    [InlineData("EndingSoon", false)]
    [InlineData(null, false)]
    public void EndsSession_OnlyForBlockingStates(string? state, bool expected) =>
        Assert.Equal(expected, ReadingSchedulePolicy.EndsSession(state));

    // ---------- Đếm đọc liên tục ----------

    [Fact]
    public void TrackReading_FirstSignal_StartsCounting()
    {
        var schedule = Schedule(breakEnabled: true);

        Assert.True(ReadingSchedulePolicy.TrackReading(schedule, Vn(23, 10, 0)));
        Assert.Equal(Vn(23, 10, 0), schedule.ContinuousReadingSince);
        Assert.Equal(Vn(23, 10, 0), schedule.LastReadingSeenAt);
    }

    [Fact]
    public void TrackReading_WithinAMinute_WritesNothing()
    {
        var schedule = Schedule(breakEnabled: true);
        ReadingSchedulePolicy.TrackReading(schedule, Vn(23, 10, 0));

        Assert.False(ReadingSchedulePolicy.TrackReading(schedule, Vn(23, 10, 0).AddSeconds(50)));
    }

    [Fact]
    public void TrackReading_TwentyMinutesStraight_StartsFiveMinuteBreak()
    {
        var schedule = Schedule(breakEnabled: true);
        var start = Vn(23, 10, 0);
        for (var minute = 0; minute < 20; minute++)
        {
            ReadingSchedulePolicy.TrackReading(schedule, start.AddMinutes(minute));
        }

        Assert.Null(schedule.BreakUntil);
        Assert.True(ReadingSchedulePolicy.TrackReading(schedule, start.AddMinutes(20)));
        Assert.Equal(start.AddMinutes(25), schedule.BreakUntil);
        Assert.Null(schedule.ContinuousReadingSince);
    }

    [Fact]
    public void TrackReading_DuringBreak_ChangesNothing()
    {
        var schedule = Schedule(breakEnabled: true);
        schedule.BreakUntil = Vn(23, 10, 5);

        Assert.False(ReadingSchedulePolicy.TrackReading(schedule, Vn(23, 10, 2)));
    }

    [Fact]
    public void TrackReading_AfterBreak_StartsNewCount()
    {
        var schedule = Schedule(breakEnabled: true);
        schedule.BreakUntil = Vn(23, 10, 5);

        Assert.True(ReadingSchedulePolicy.TrackReading(schedule, Vn(23, 10, 5)));
        Assert.Null(schedule.BreakUntil);
        Assert.Equal(Vn(23, 10, 5), schedule.ContinuousReadingSince);
    }

    [Fact]
    public void TrackReading_PauseAsLongAsBreak_ResetsCount()
    {
        var schedule = Schedule(breakEnabled: true);
        schedule.ContinuousReadingSince = Vn(23, 10, 0);
        schedule.LastReadingSeenAt = Vn(23, 10, 10);

        Assert.True(ReadingSchedulePolicy.TrackReading(schedule, Vn(23, 10, 15)));
        Assert.Equal(Vn(23, 10, 15), schedule.ContinuousReadingSince);
    }

    [Fact]
    public void TrackReading_OneMinuteBreakSetting_UsesTwoMinuteGap()
    {
        var schedule = Schedule(breakEnabled: true);
        schedule.BreakDurationMinutes = 1;
        schedule.ContinuousReadingSince = Vn(23, 10, 0);
        schedule.LastReadingSeenAt = Vn(23, 10, 5);

        Assert.True(ReadingSchedulePolicy.TrackReading(schedule, Vn(23, 10, 6)));
        Assert.Equal(Vn(23, 10, 0), schedule.ContinuousReadingSince);
    }

    [Fact]
    public void TrackReading_BreakDisabled_DoesNothing() =>
        Assert.False(ReadingSchedulePolicy.TrackReading(Schedule(breakEnabled: false), Vn(23, 10, 0)));

    // ---------- Validate ----------

    [Fact]
    public void Validate_FullSchedule_IsValid() =>
        Assert.Null(ReadingSchedulePolicy.Validate(Settings(
            [In(ScheduleDayType.Weekday, "07:00", "20:30"), In(ScheduleDayType.Weekend, "08:00", "11:00"),
             In(ScheduleDayType.Weekend, "15:00", "20:00")],
            bedtimeWeekday: "20:30", bedtimeWeekend: "21:00")));

    [Fact]
    public void Validate_NothingSet_IsValid() => Assert.Null(ReadingSchedulePolicy.Validate(Settings()));

    [Theory]
    [InlineData("08:00", "08:00")]
    [InlineData("09:00", "08:00")]
    public void Validate_StartNotBeforeEnd_IsRejected(string start, string end) =>
        Assert.Equal(ReadingSchedulePolicy.StartBeforeEndMessage,
            ReadingSchedulePolicy.Validate(Settings([In(ScheduleDayType.Weekday, start, end)])));

    [Fact]
    public void Validate_ThreeWindowsSameDayType_IsRejected() =>
        Assert.Equal(ReadingSchedulePolicy.MaxWindowsMessage, ReadingSchedulePolicy.Validate(Settings(
            [In(ScheduleDayType.Weekday, "06:00", "07:00"), In(ScheduleDayType.Weekday, "12:00", "13:00"),
             In(ScheduleDayType.Weekday, "18:00", "19:00")])));

    [Fact]
    public void Validate_OverlappingWeekday_IsRejected() =>
        Assert.Equal("Hai khung giờ ngày thường đang chồng nhau.", ReadingSchedulePolicy.Validate(Settings(
            [In(ScheduleDayType.Weekday, "07:00", "09:00"), In(ScheduleDayType.Weekday, "08:30", "10:00")])));

    [Fact]
    public void Validate_OverlappingWeekend_IsRejected() =>
        Assert.Equal("Hai khung giờ cuối tuần đang chồng nhau.", ReadingSchedulePolicy.Validate(Settings(
            [In(ScheduleDayType.Weekend, "15:00", "20:00"), In(ScheduleDayType.Weekend, "08:00", "15:30")])));

    [Fact]
    public void Validate_TouchingWindows_AreValid() =>
        Assert.Null(ReadingSchedulePolicy.Validate(Settings(
            [In(ScheduleDayType.Weekday, "07:00", "08:00"), In(ScheduleDayType.Weekday, "08:00", "09:00")])));

    [Fact]
    public void Validate_UnknownDayType_IsRejected() =>
        Assert.Equal(ReadingSchedulePolicy.InvalidDayTypeMessage,
            ReadingSchedulePolicy.Validate(Settings([In((ScheduleDayType)0, "07:00", "08:00")])));

    [Theory]
    [InlineData(14)]
    [InlineData(61)]
    public void Validate_BreakAfterOutOfRange_IsRejected(int minutes) =>
        Assert.Equal(ReadingSchedulePolicy.BreakAfterMessage, ReadingSchedulePolicy.Validate(Settings(breakAfter: minutes)));

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void Validate_BreakDurationOutOfRange_IsRejected(int minutes) =>
        Assert.Equal(ReadingSchedulePolicy.BreakDurationMessage, ReadingSchedulePolicy.Validate(Settings(breakDuration: minutes)));

    [Fact]
    public void Validate_BreakWithoutActivity_IsRejected() =>
        Assert.Equal(ReadingSchedulePolicy.BreakActivityRequiredMessage,
            ReadingSchedulePolicy.Validate(Settings(eyes: false, water: false, move: false)));

    [Fact]
    public void Validate_BreakOffWithoutActivity_IsValid() =>
        Assert.Null(ReadingSchedulePolicy.Validate(Settings(breakEnabled: false, eyes: false, water: false, move: false)));

    [Theory]
    [InlineData(9, 120)]
    [InlineData(60, 241)]
    public void Validate_ScreenLimitOutOfRange_IsRejected(int weekday, int weekend) =>
        Assert.Equal(ReadingSchedulePolicy.ScreenLimitMessage,
            ReadingSchedulePolicy.Validate(Settings(screenWeekday: weekday, screenWeekend: weekend)));

    [Theory]
    [InlineData("17:59", null)]
    [InlineData(null, "23:31")]
    public void Validate_BedtimeOutOfRange_IsRejected(string? weekday, string? weekend) =>
        Assert.Equal(ReadingSchedulePolicy.BedtimeRangeMessage,
            ReadingSchedulePolicy.Validate(Settings(bedtimeWeekday: weekday, bedtimeWeekend: weekend)));

    [Fact]
    public void Validate_BedtimeBoundaries_AreValid() =>
        Assert.Null(ReadingSchedulePolicy.Validate(Settings(bedtimeWeekday: "18:00", bedtimeWeekend: "23:30")));

    // ---------- Gia hạn khi nhiều mốc chặn chồng nhau ----------

    [Fact]
    public void Evaluate_WindowEndsBeforeBedtime_GraceDoesNotExceedTenMinutesFromFirstBlock()
    {
        var windows = new[] { W(ScheduleDayType.Weekday, "07:00", "20:25") };
        var started = Vn(23, 20, 0);

        var grace = ReadingSchedulePolicy.Evaluate(WithBedtime(), windows, Vn(23, 20, 33), started);
        var over = ReadingSchedulePolicy.Evaluate(WithBedtime(), windows, Vn(23, 20, 36), started);

        Assert.Equal(ReadingTimeState.Grace, grace.State);
        Assert.Equal(ReadingTimeEndReason.Window, grace.EndReason);
        Assert.Equal(Vn(23, 20, 35), grace.GraceEndsAt);
        Assert.Equal(ReadingTimeState.Bedtime, over.State);
    }

    [Fact]
    public void Evaluate_WindowEndsBeforeScreenLimit_GraceDoesNotExceedTenMinutesFromFirstBlock()
    {
        var windows = new[] { W(ScheduleDayType.Weekday, "07:00", "10:00") };
        var started = Vn(23, 9, 30);

        var grace = ReadingSchedulePolicy.Evaluate(WithScreenLimit(48), windows, Vn(23, 10, 8), started);
        var over = ReadingSchedulePolicy.Evaluate(WithScreenLimit(51), windows, Vn(23, 10, 11), started);

        Assert.Equal(ReadingTimeState.Grace, grace.State);
        Assert.Equal(ReadingTimeEndReason.Window, grace.EndReason);
        Assert.Equal(Vn(23, 10, 10), grace.GraceEndsAt);
        Assert.Equal(ReadingTimeState.ScreenLimitReached, over.State);
    }

    [Fact]
    public void Evaluate_ScreenLimitGrace_EndsAtMomentLimitWasHit()
    {
        var status = ReadingSchedulePolicy.Evaluate(WithScreenLimit(50), [], Vn(23, 10, 0), Vn(23, 9, 30));

        Assert.Equal(ReadingTimeState.Grace, status.State);
        Assert.Equal(Vn(23, 9, 55), status.EndsAt);
    }

    [Fact]
    public void Validate_InclusiveBounds_AreValid()
    {
        Assert.Null(ReadingSchedulePolicy.Validate(
            Settings(breakAfter: 15, breakDuration: 1, screenWeekday: 10, screenWeekend: 240)));
        Assert.Null(ReadingSchedulePolicy.Validate(Settings(breakAfter: 60, breakDuration: 15)));
    }

    [Fact]
    public void Evaluate_FridayBedtime_WakesAtSaturdaysFirstWeekendWindow()
    {
        var status = ReadingSchedulePolicy.Evaluate(WithBedtime(), Default, Vn(21, 7, 0));

        Assert.Equal(ReadingTimeState.Bedtime, status.State);
        Assert.Equal(Vn(21, 8, 0), status.NextAllowedAt);
    }

    // ---------- Câu báo ----------

    [Fact]
    public void ParentMessages_NameTheirTimes()
    {
        Assert.Equal("Ngoài khung giờ đọc của bé (khung kế tiếp: Thứ 7 08:00).", ReadingSchedulePolicy.ParentOutsideMessage(Vn(28, 8, 0)));
        Assert.Equal("Ngoài khung giờ đọc của bé (khung kế tiếp: Chủ nhật 08:00).", ReadingSchedulePolicy.ParentOutsideMessage(Vn(29, 8, 0)));
        Assert.Equal("Ngoài khung giờ đọc của bé (khung kế tiếp: Thứ 2 07:00).", ReadingSchedulePolicy.ParentOutsideMessage(Vn(23, 7, 0)));
        Assert.Equal("Ngoài khung giờ đọc của bé.", ReadingSchedulePolicy.ParentOutsideMessage(null));
        Assert.Equal("Đã qua giờ đi ngủ của bé (20:30).", ReadingSchedulePolicy.ParentBedtimeMessage(Vn(23, 20, 30)));
        Assert.Equal("Bé đã dùng hết 45 phút hôm nay.", ReadingSchedulePolicy.ParentScreenLimitMessage(45));
    }

    [Fact]
    public void BreakMessage_RoundsRemainingMinutesUp() =>
        Assert.Equal("Bé nghỉ mắt một chút nhé, còn 5 phút.",
            ReadingSchedulePolicy.BreakMessage(Vn(23, 10, 4).AddSeconds(30), Vn(23, 10, 0)));

    [Fact]
    public void MessageFor_BreakAppendsActivity()
    {
        var status = new ReadingTimeStatus
        {
            State = ReadingTimeState.Break, BreakUntil = Vn(23, 10, 3), BreakActivity = BreakActivity.Water
        };

        Assert.Equal("Bé nghỉ mắt một chút nhé, còn 3 phút. Bé uống một ngụm nước nhé!",
            ReadingSchedulePolicy.MessageFor(status, Vn(23, 10, 0)));
    }

    [Fact]
    public void MessageFor_AllowedHasNoMessage() =>
        Assert.Null(ReadingSchedulePolicy.MessageFor(ReadingTimeStatus.Free, Vn(23, 10, 0)));
}
