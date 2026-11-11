using StoryPlatform.Application.Common.Time;
using Xunit;

namespace StoryPlatform.UnitTests.Common;

public sealed class VietnamTimeTests
{
    private static DateTime Utc(string value) =>
        DateTime.SpecifyKind(DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);

    [Fact]
    public void StartOfWeekUtc_SundayLateEveningVietnam_StaysInSameWeek() =>
        Assert.Equal(Utc("2026-11-08T17:00:00"), VietnamTime.StartOfWeekUtc(Utc("2026-11-15T16:59:00")));

    [Fact]
    public void StartOfWeekUtc_MondayMidnightVietnam_StartsNewWeek() =>
        Assert.Equal(Utc("2026-11-15T17:00:00"), VietnamTime.StartOfWeekUtc(Utc("2026-11-15T17:00:00")));

    [Fact]
    public void StartOfNextWeekUtc_IsTheFollowingMonday() =>
        Assert.Equal(Utc("2026-11-15T17:00:00"), VietnamTime.StartOfNextWeekUtc(Utc("2026-11-11T03:00:00")));

    [Fact]
    public void StartOfDayUtc_AfterVietnamMidnight_IsSameUtcEvening() =>
        Assert.Equal(Utc("2026-11-11T17:00:00"), VietnamTime.StartOfDayUtc(Utc("2026-11-11T18:00:00")));

    [Fact]
    public void StartOfNextMonthUtc_AlreadyNextMonthInVietnam_SkipsToTheMonthAfter() =>
        Assert.Equal(Utc("2026-12-31T17:00:00"), VietnamTime.StartOfNextMonthUtc(Utc("2026-11-30T17:30:00")));

    [Fact]
    public void StartOfMonthUtc_IsFirstDayVietnam() =>
        Assert.Equal(Utc("2026-10-31T17:00:00"), VietnamTime.StartOfMonthUtc(Utc("2026-11-11T03:00:00")));
}
