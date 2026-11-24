using System.Globalization;
using StoryPlatform.Application.Features.ChildProfiles.ReadingTime;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.ReadingTime;

public sealed class ReadingHabitPolicyTests
{
    // Tháng 11/2026: 23 = Thứ Hai, 26 = Thứ Năm, 27 = Thứ Sáu, 28 = Thứ Bảy, 29 = Chủ nhật. 1/12 = Thứ Ba.
    private static DateTime Vn(int month, int day, int hour, int minute) =>
        new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Utc).AddHours(-7);

    private static DateTime Nov(int day, int hour, int minute) => Vn(11, day, hour, minute);

    private static TimeOnly T(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    private static ReadingSchedule Schedule(
        bool habit = true, string? reminderWeekday = null, string? reminderWeekend = null,
        string? bedtimeWeekday = null, string? bedtimeWeekend = null) => new()
    {
        Id = 1, ChildProfileId = 1, HabitEnabled = habit,
        ReadingReminderWeekday = reminderWeekday == null ? null : T(reminderWeekday),
        ReadingReminderWeekend = reminderWeekend == null ? null : T(reminderWeekend),
        BedtimeWeekday = bedtimeWeekday == null ? null : T(bedtimeWeekday),
        BedtimeWeekend = bedtimeWeekend == null ? null : T(bedtimeWeekend)
    };

    private static ReadingScheduleWindow W(ScheduleDayType type, string start, string end) => new()
    {
        ReadingScheduleId = 1, DayType = type, StartTime = T(start), EndTime = T(end)
    };

    private static ScheduleWindowInput In(ScheduleDayType type, string start, string end) => new(type, T(start), T(end));

    private static ScheduleSettingsInput Settings(
        IReadOnlyCollection<ScheduleWindowInput>? windows = null, string? bedtimeWeekday = null,
        string? bedtimeWeekend = null, string? reminderWeekday = null, string? reminderWeekend = null,
        int? goal = null) =>
        new(true, 20, 5, true, true, true, 60, 120,
            bedtimeWeekday == null ? null : T(bedtimeWeekday), bedtimeWeekend == null ? null : T(bedtimeWeekend),
            windows ?? [],
            reminderWeekday == null ? null : T(reminderWeekday), reminderWeekend == null ? null : T(reminderWeekend),
            goal);

    // ---------- Validate ----------

    [Fact]
    public void Validate_NoHabitSettings_IsValid() => Assert.Null(ReadingSchedulePolicy.Validate(Settings()));

    [Theory]
    [InlineData("05:59")]
    [InlineData("00:00")]
    public void Validate_ReminderBeforeSix_IsRejected(string time) =>
        Assert.Equal(ReadingSchedulePolicy.ReminderEarliestMessage,
            ReadingSchedulePolicy.Validate(Settings(reminderWeekday: time)));

    [Fact]
    public void Validate_ReminderAtSix_IsValid() =>
        Assert.Null(ReadingSchedulePolicy.Validate(Settings(reminderWeekend: "06:00")));

    [Fact]
    public void Validate_ReminderNotBeforeBedtime_IsRejected()
    {
        Assert.Equal(ReadingSchedulePolicy.ReminderBeforeBedtimeMessage,
            ReadingSchedulePolicy.Validate(Settings(bedtimeWeekday: "20:30", reminderWeekday: "20:30")));
        Assert.Null(ReadingSchedulePolicy.Validate(Settings(bedtimeWeekday: "20:30", reminderWeekday: "20:29")));
    }

    [Fact]
    public void Validate_WeekendReminderIsComparedWithWeekendBedtime()
    {
        Assert.Null(ReadingSchedulePolicy.Validate(
            Settings(bedtimeWeekday: "19:00", bedtimeWeekend: "21:00", reminderWeekend: "20:00")));
        Assert.Equal(ReadingSchedulePolicy.ReminderBeforeBedtimeMessage,
            ReadingSchedulePolicy.Validate(Settings(bedtimeWeekday: "21:00", bedtimeWeekend: "19:00", reminderWeekend: "20:00")));
    }

    [Theory]
    [InlineData("07:59", false)]
    [InlineData("08:00", true)]
    [InlineData("08:59", true)]
    [InlineData("09:00", false)]
    public void Validate_ReminderMustBeInsideAWindowOfItsDayType(string time, bool valid)
    {
        var windows = new[] { In(ScheduleDayType.Weekday, "08:00", "09:00") };

        var error = ReadingSchedulePolicy.Validate(Settings(windows, reminderWeekday: time));

        Assert.Equal(valid ? null : ReadingSchedulePolicy.ReminderInWindowMessage, error);
    }

    [Fact]
    public void Validate_DayTypeWithoutWindows_ReminderAnywhereAfterSix() =>
        Assert.Null(ReadingSchedulePolicy.Validate(
            Settings([In(ScheduleDayType.Weekday, "08:00", "09:00")], reminderWeekend: "15:00")));

    [Fact]
    public void Validate_TouchingWindows_ReminderAtJoinIsValid() =>
        Assert.Null(ReadingSchedulePolicy.Validate(Settings(
            [In(ScheduleDayType.Weekday, "07:00", "08:00"), In(ScheduleDayType.Weekday, "08:00", "09:00")],
            reminderWeekday: "08:00")));

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(-1)]
    public void Validate_GoalOutOfRange_IsRejected(int goal) =>
        Assert.Equal(ReadingSchedulePolicy.WeeklyGoalMessage, ReadingSchedulePolicy.Validate(Settings(goal: goal)));

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void Validate_GoalBounds_AreValid(int goal) =>
        Assert.Null(ReadingSchedulePolicy.Validate(Settings(goal: goal)));

    // ---------- BuildReminders ----------

    [Fact]
    public void BuildReminders_HabitDisabled_IsEmpty() =>
        Assert.Empty(ReadingSchedulePolicy.BuildReminders(
            Schedule(habit: false, reminderWeekday: "19:00"), Nov(23, 10, 0), readToday: false));

    [Fact]
    public void BuildReminders_NoTimes_IsEmpty() =>
        Assert.Empty(ReadingSchedulePolicy.BuildReminders(Schedule(), Nov(23, 10, 0), readToday: false));

    [Fact]
    public void BuildReminders_WeekdayOnly_ListsRemainingWeekdaysOfNextSevenDays()
    {
        var reminders = ReadingSchedulePolicy.BuildReminders(
            Schedule(reminderWeekday: "19:00"), Nov(23, 10, 0), readToday: false);

        // Hôm nay Thứ Hai đến Chủ nhật 29/11: Thứ Hai–Thứ Sáu.
        Assert.Equal([Nov(23, 19, 0), Nov(24, 19, 0), Nov(25, 19, 0), Nov(26, 19, 0), Nov(27, 19, 0)], reminders);
    }

    [Fact]
    public void BuildReminders_UsesWeekendTimeOnWeekend_AndSkipsPastTimeToday()
    {
        // Thứ Sáu 27/11 20:00: giờ nhắc 19:00 hôm nay đã qua.
        var reminders = ReadingSchedulePolicy.BuildReminders(
            Schedule(reminderWeekday: "19:00", reminderWeekend: "09:00"), Nov(27, 20, 0), readToday: false);

        Assert.Equal(
            [Nov(28, 9, 0), Nov(29, 9, 0), Vn(11, 30, 19, 0), Vn(12, 1, 19, 0), Vn(12, 2, 19, 0), Vn(12, 3, 19, 0)],
            reminders);
    }

    [Fact]
    public void BuildReminders_ReminderExactlyNow_IsSkipped()
    {
        var reminders = ReadingSchedulePolicy.BuildReminders(
            Schedule(reminderWeekday: "19:00"), Nov(23, 19, 0), readToday: false);

        Assert.Equal(Nov(24, 19, 0), reminders[0]);
    }

    [Fact]
    public void BuildReminders_AlreadyReadToday_SkipsTodayOnly()
    {
        var reminders = ReadingSchedulePolicy.BuildReminders(
            Schedule(reminderWeekday: "19:00"), Nov(23, 10, 0), readToday: true);

        Assert.Equal([Nov(24, 19, 0), Nov(25, 19, 0), Nov(26, 19, 0), Nov(27, 19, 0)], reminders);
    }

    [Fact]
    public void BuildReminders_OnlyWeekendTime_OnWeekdayStartListsWeekendDays()
    {
        var reminders = ReadingSchedulePolicy.BuildReminders(
            Schedule(reminderWeekend: "09:00"), Nov(23, 10, 0), readToday: false);

        Assert.Equal([Nov(28, 9, 0), Nov(29, 9, 0)], reminders);
    }

    // ---------- BuildQuietPeriods ----------

    [Fact]
    public void BuildQuietPeriods_HabitDisabled_IsEmpty() =>
        Assert.Empty(ReadingSchedulePolicy.BuildQuietPeriods(
            Schedule(habit: false, bedtimeWeekday: "20:30"), [], Nov(23, 10, 0)));

    [Fact]
    public void BuildQuietPeriods_NoBedtime_IsEmpty() =>
        Assert.Empty(ReadingSchedulePolicy.BuildQuietPeriods(Schedule(), [], Nov(23, 10, 0)));

    [Fact]
    public void BuildQuietPeriods_SevenNights_WakeAtSixWithoutWindows()
    {
        var periods = ReadingSchedulePolicy.BuildQuietPeriods(
            Schedule(bedtimeWeekday: "20:30", bedtimeWeekend: "21:00"), [], Nov(23, 10, 0));

        // Đêm Chủ nhật 22/11 đã hết lúc 06:00 sáng nay; còn 7 đêm từ Thứ Hai 23/11 đến Chủ nhật 29/11.
        Assert.Equal(7, periods.Count);
        Assert.Equal(new QuietPeriod(Nov(23, 20, 30), Nov(24, 6, 0)), periods[0]);
        Assert.Equal(new QuietPeriod(Nov(28, 21, 0), Nov(29, 6, 0)), periods[5]);
        Assert.Equal(new QuietPeriod(Nov(29, 21, 0), Vn(11, 30, 6, 0)), periods[6]);
    }

    [Fact]
    public void BuildQuietPeriods_WakeTimeIsFirstWindowOfNextDay()
    {
        var periods = ReadingSchedulePolicy.BuildQuietPeriods(
            Schedule(bedtimeWeekday: "20:30"), [W(ScheduleDayType.Weekend, "08:00", "11:00")], Nov(23, 10, 0));

        // Đêm Thứ Sáu 27/11 thức dậy đầu khung cuối tuần Thứ Bảy 08:00.
        Assert.Contains(new QuietPeriod(Nov(27, 20, 30), Nov(28, 8, 0)), periods);
    }

    [Fact]
    public void BuildQuietPeriods_NightInProgress_IsIncluded()
    {
        var periods = ReadingSchedulePolicy.BuildQuietPeriods(
            Schedule(bedtimeWeekday: "20:30"), [], Nov(24, 2, 0));

        Assert.Equal(new QuietPeriod(Nov(23, 20, 30), Nov(24, 6, 0)), periods[0]);
    }

    [Fact]
    public void BuildQuietPeriods_OnlyWeekdayBedtime_SkipsWeekendNights()
    {
        var periods = ReadingSchedulePolicy.BuildQuietPeriods(
            Schedule(bedtimeWeekday: "20:30"), [], Nov(23, 10, 0));

        Assert.Equal(5, periods.Count);
        Assert.DoesNotContain(periods, period => period.FromUtc == Nov(28, 20, 30));
    }
}
