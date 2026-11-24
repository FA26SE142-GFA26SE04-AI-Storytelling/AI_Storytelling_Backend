using System.Globalization;
using StoryPlatform.Application.Features.ChildProfiles.ReadingTime;
using StoryPlatform.Application.Features.ChildProfiles.ReadingTime.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ReadingSessions;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.ReadingTime;

public sealed class ReadingHabitServiceTests
{
    private const int ChildId = 1;
    // Thứ Năm 26/11/2026, 10:00 giờ Việt Nam. Tuần này bắt đầu Thứ Hai 23/11 00:00 giờ Việt Nam.
    private static readonly DateTime Now = new(2026, 11, 26, 3, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly ManualTimeProvider _clock = new(Now);
    private readonly ReadingHabitService _sut;
    private int _nextSessionId = 1;

    public ReadingHabitServiceTests()
    {
        _sut = new ReadingHabitService(_uow, _clock);
    }

    private static DateTime Vn(int month, int day, int hour, int minute) =>
        new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Utc).AddHours(-7);

    private static TimeOnly T(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    private void Done(DateTime completedAtUtc, int childId = ChildId, SessionStatus status = SessionStatus.Completed,
        bool hasCompletedAt = true) => _uow.Seed(new ReadingSession
    {
        Id = _nextSessionId++, ChildProfileId = childId, StoryId = 1, Status = status,
        StartedAt = completedAtUtc.AddMinutes(-10), CompletedAt = hasCompletedAt ? completedAtUtc : null
    });

    private ReadingSchedule Schedule(bool habit = true, int? goal = 3) => _uow.Seed(new ReadingSchedule
    {
        Id = 1, ChildProfileId = ChildId, HabitEnabled = habit, WeeklyGoalDays = goal,
        ReadingReminderWeekday = T("19:00"), ReadingReminderWeekend = T("09:00"),
        BedtimeWeekday = T("20:30"), BedtimeWeekend = T("21:00")
    });

    // ---------- GetProgressAsync ----------

    [Fact]
    public async Task Progress_CountsDistinctVietnamDaysOfThisWeekOnly()
    {
        Done(Vn(11, 23, 9, 0));
        Done(Vn(11, 23, 15, 0));     // cùng ngày Thứ Hai: chỉ tính 1
        Done(Vn(11, 25, 20, 0));     // Thứ Tư
        Done(Vn(11, 22, 23, 59));    // Chủ nhật tuần trước: không tính
        Done(Vn(11, 23, 0, 0));      // đúng 00:00 Thứ Hai: tính (cùng ngày Thứ Hai)

        var progress = await _sut.GetProgressAsync(ChildId, 3);

        Assert.Equal(2, progress.DaysReadThisWeek);
    }

    [Fact]
    public async Task Progress_IgnoresUnfinishedOtherChildAndMissingCompletedAt()
    {
        Done(Vn(11, 24, 10, 0), status: SessionStatus.Reading);
        Done(Vn(11, 24, 10, 0), status: SessionStatus.Abandoned);
        Done(Vn(11, 24, 10, 0), status: SessionStatus.ForceExited);
        Done(Vn(11, 24, 10, 0), childId: 2);
        Done(Vn(11, 24, 10, 0), hasCompletedAt: false);

        Assert.Equal(0, (await _sut.GetProgressAsync(ChildId, 3)).DaysReadThisWeek);
    }

    [Fact]
    public async Task Progress_NextWeekBoundaryIsMondayInVietnam()
    {
        Done(Vn(11, 30, 0, 0)); // 00:00 Thứ Hai 30/11 giờ Việt Nam: thuộc tuần sau

        Assert.Equal(0, (await _sut.GetProgressAsync(ChildId, null)).DaysReadThisWeek);
    }

    [Fact]
    public async Task Progress_GoalMetOnlyWhenEnoughDays()
    {
        Done(Vn(11, 23, 9, 0));
        Done(Vn(11, 25, 9, 0));

        Assert.True((await _sut.GetProgressAsync(ChildId, 2)).GoalMetThisWeek);
        Assert.False((await _sut.GetProgressAsync(ChildId, 3)).GoalMetThisWeek);
        Assert.False((await _sut.GetProgressAsync(ChildId, null)).GoalMetThisWeek);
    }

    [Fact]
    public async Task Progress_StreakIsAliveYesterdayAndOverAfterTwoDays()
    {
        var achievement = _uow.Seed(new Achievement { Id = 1, ChildProfileId = ChildId, StreakDays = 5 });
        achievement.LastStreakDate = new DateOnly(2026, 11, 25);
        Assert.Equal(5, (await _sut.GetProgressAsync(ChildId, null)).StreakDays);

        achievement.LastStreakDate = new DateOnly(2026, 11, 24);
        Assert.Equal(0, (await _sut.GetProgressAsync(ChildId, null)).StreakDays);
    }

    // ---------- GetChildHabitAsync ----------

    [Fact]
    public async Task ChildHabit_NoSchedule_IsDisabledButKeepsStreakAndDays()
    {
        _uow.Seed(new Achievement { Id = 1, ChildProfileId = ChildId, StreakDays = 4, LastStreakDate = new DateOnly(2026, 11, 26) });
        Done(Vn(11, 24, 10, 0));

        var habit = await _sut.GetChildHabitAsync(ChildId);

        Assert.False(habit.Enabled);
        Assert.Equal(4, habit.StreakDays);
        Assert.Equal(1, habit.DaysReadThisWeek);
        Assert.Null(habit.WeeklyGoalDays);
        Assert.False(habit.GoalMetThisWeek);
        Assert.Empty(habit.Reminders);
        Assert.Empty(habit.QuietPeriods);
    }

    [Fact]
    public async Task ChildHabit_HabitSwitchedOff_HidesGoalAndSchedules()
    {
        Schedule(habit: false, goal: 3);

        var habit = await _sut.GetChildHabitAsync(ChildId);

        Assert.False(habit.Enabled);
        Assert.Null(habit.WeeklyGoalDays);
        Assert.Empty(habit.Reminders);
        Assert.Empty(habit.QuietPeriods);
    }

    [Fact]
    public async Task ChildHabit_Enabled_ListsSevenDaysOfRemindersAndNights()
    {
        Schedule();

        var habit = await _sut.GetChildHabitAsync(ChildId);

        Assert.True(habit.Enabled);
        Assert.Equal(3, habit.WeeklyGoalDays);
        // Thứ Năm 26, Thứ Sáu 27 (19:00), Thứ Bảy 28, Chủ nhật 29 (09:00), Thứ Hai 30, Thứ Ba 1/12, Thứ Tư 2/12 (19:00).
        Assert.Equal(
            [Vn(11, 26, 19, 0), Vn(11, 27, 19, 0), Vn(11, 28, 9, 0), Vn(11, 29, 9, 0), Vn(11, 30, 19, 0), Vn(12, 1, 19, 0), Vn(12, 2, 19, 0)],
            habit.Reminders.Select(reminder => reminder.At));
        Assert.All(habit.Reminders, reminder => Assert.Equal(ReadingSchedulePolicy.ReadingReminderMessage, reminder.Message));
        Assert.Equal(7, habit.QuietPeriods.Count);
        Assert.Equal(Vn(11, 26, 20, 30), habit.QuietPeriods[0].From);
        Assert.Equal(Vn(11, 27, 6, 0), habit.QuietPeriods[0].To);
        Assert.Equal(Vn(11, 28, 21, 0), habit.QuietPeriods[2].From);
    }

    [Fact]
    public async Task ChildHabit_AlreadyReadToday_SkipsTodaysReminder()
    {
        Schedule();
        Done(Vn(11, 26, 8, 0));

        var habit = await _sut.GetChildHabitAsync(ChildId);

        Assert.Equal(Vn(11, 27, 19, 0), habit.Reminders[0].At);
        Assert.Equal(6, habit.Reminders.Count);
    }

    [Fact]
    public async Task ChildHabit_NightWindowEndsAtFirstWindowOfNextDay()
    {
        Schedule();
        _uow.Seed(new ReadingScheduleWindow
        {
            Id = 1, ReadingScheduleId = 1, DayType = ScheduleDayType.Weekend, Slot = 1,
            StartTime = T("08:00"), EndTime = T("11:00")
        });

        var habit = await _sut.GetChildHabitAsync(ChildId);

        Assert.Equal(Vn(11, 28, 8, 0), habit.QuietPeriods[1].To); // đêm Thứ Sáu thức dậy lúc 08:00 Thứ Bảy
    }
}
