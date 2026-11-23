using System.Globalization;
using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.ReadingTime;
using StoryPlatform.Application.Features.ChildProfiles.ReadingTime.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.ReadingTime.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ReadingSessions;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.ReadingTime;

public sealed class ReadingScheduleServiceTests
{
    private const int OwnerId = 7;
    private const int ChildId = 1;
    // Thứ Hai 23/11/2026, 10:00 giờ Việt Nam.
    private static readonly DateTime Now = new(2026, 11, 23, 3, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly Mock<IChildProfileAccessGuard> _guard = new();
    private readonly Mock<IAuditLogWriter> _audit = new();
    private readonly ManualTimeProvider _clock = new(Now);
    private readonly ChildProfile _child;
    private readonly ReadingScheduleService _sut;

    public ReadingScheduleServiceTests()
    {
        _child = _uow.Seed(new ChildProfile
        {
            Id = ChildId, OwnerUserId = OwnerId, Nickname = "Bé An", AgeBand = AgeBand.Age_6_8,
            Status = ChildProfileStatus.Active
        });
        _sut = new ReadingScheduleService(_uow, _guard.Object, _audit.Object, _clock);
    }

    private static TimeOnly T(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    private ReadingSchedule SeedSchedule(bool breakEnabled, params (ScheduleDayType Type, string Start, string End)[] windows)
    {
        var schedule = _uow.Seed(new ReadingSchedule
        {
            Id = 1, ChildProfileId = ChildId, Enabled = true, BreakEnabled = breakEnabled,
            BreakAfterMinutes = 20, BreakDurationMinutes = 5
        });
        var id = 0;
        foreach (var window in windows)
        {
            id++;
            _uow.Seed(new ReadingScheduleWindow
            {
                Id = id, ReadingScheduleId = 1, DayType = window.Type, Slot = id,
                StartTime = T(window.Start), EndTime = T(window.End)
            });
        }

        return schedule;
    }

    private static SetReadingScheduleRequestDto Request(params (ScheduleDayType Type, string Start, string End)[] windows) => new()
    {
        Enabled = true, BedtimeWeekday = "20:30", BedtimeWeekend = "21:00",
        BreakEnabled = true, BreakAfterMinutes = 20, BreakDurationMinutes = 5,
        BreakRemindEyes = true, BreakRemindWater = false, BreakRemindMove = true,
        ScreenLimitEnabled = true, ScreenLimitWeekdayMinutes = 45, ScreenLimitWeekendMinutes = 90,
        Windows = windows.Select(window => new SetReadingScheduleWindowRequestDto
        {
            DayType = window.Type, StartTime = window.Start, EndTime = window.End
        }).ToList()
    };

    private static string[] Describe(ReadingScheduleDto dto) =>
        dto.Windows.Select(window => $"{window.DayType} {window.Slot} {window.StartTime}-{window.EndTime}").ToArray();

    // ---------- GetAsync ----------

    [Fact]
    public async Task Get_NoSchedule_Age6To8_ReturnsSuggestion()
    {
        var dto = await _sut.GetAsync(ChildId, OwnerId);

        Assert.True(dto.IsSuggested);
        Assert.False(dto.Enabled);
        Assert.Equal("20:30", dto.BedtimeWeekday);
        Assert.Equal("20:30", dto.BedtimeWeekend);
        Assert.True(dto.BreakEnabled);
        Assert.Equal(20, dto.BreakAfterMinutes);
        Assert.Equal(5, dto.BreakDurationMinutes);
        Assert.True(dto.BreakRemindEyes && dto.BreakRemindWater && dto.BreakRemindMove);
        Assert.True(dto.ScreenLimitEnabled);
        Assert.Equal(45, dto.ScreenLimitWeekdayMinutes);
        Assert.Equal(90, dto.ScreenLimitWeekendMinutes);
        Assert.Equal(new[] { "Weekday 1 18:00-20:00", "Weekend 1 08:00-11:00", "Weekend 2 15:00-20:00" }, Describe(dto));
        Assert.Empty(_uow.Items<ReadingSchedule>());
    }

    [Fact]
    public async Task Get_NoSchedule_Age9To10_ReturnsSuggestion()
    {
        _child.AgeBand = AgeBand.Age_9_10;

        var dto = await _sut.GetAsync(ChildId, OwnerId);

        Assert.Equal("21:00", dto.BedtimeWeekday);
        Assert.Equal(30, dto.BreakAfterMinutes);
        Assert.Equal(60, dto.ScreenLimitWeekdayMinutes);
        Assert.Equal(120, dto.ScreenLimitWeekendMinutes);
        Assert.Equal(new[] { "Weekday 1 18:00-20:30", "Weekend 1 08:00-11:30", "Weekend 2 15:00-20:30" }, Describe(dto));
    }

    [Fact]
    public async Task Get_NotOwner_IsForbidden()
    {
        _guard.Setup(guard => guard.EnsureOwnerAsync(ChildId, 99, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Bạn không có quyền với hồ sơ trẻ này."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetAsync(ChildId, 99));
    }

    // ---------- SetAsync ----------

    [Fact]
    public async Task Set_New_SavesEverythingWithSlotsOrderedByStart()
    {
        var dto = await _sut.SetAsync(ChildId, OwnerId, Request(
            (ScheduleDayType.Weekend, "15:00", "20:00"),
            (ScheduleDayType.Weekend, "08:00", "11:00"),
            (ScheduleDayType.Weekday, "18:00", "20:00")));

        Assert.False(dto.IsSuggested);
        Assert.Equal(new[] { "Weekday 1 18:00-20:00", "Weekend 1 08:00-11:00", "Weekend 2 15:00-20:00" }, Describe(dto));
        var schedule = Assert.Single(_uow.Items<ReadingSchedule>());
        Assert.Equal(T("20:30"), schedule.BedtimeWeekday);
        Assert.Equal(T("21:00"), schedule.BedtimeWeekend);
        Assert.False(schedule.BreakRemindWater);
        Assert.True(schedule.ScreenLimitEnabled);
        Assert.Equal(45, schedule.ScreenLimitWeekdayMinutes);
        Assert.All(_uow.Items<ReadingScheduleWindow>(), window => Assert.Equal(schedule.Id, window.ReadingScheduleId));
        _audit.Verify(audit => audit.LogAsync(OwnerId, "SET_READING_SCHEDULE", nameof(ReadingSchedule), schedule.Id,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Set_EmptyBedtime_IsStoredAsNull()
    {
        var request = Request();
        request.BedtimeWeekday = null;
        request.BedtimeWeekend = "";

        var dto = await _sut.SetAsync(ChildId, OwnerId, request);

        Assert.Null(dto.BedtimeWeekday);
        Assert.Null(dto.BedtimeWeekend);
    }

    [Fact]
    public async Task Set_Existing_ReplacesWindowsClearsBreakStateKeepsUsage()
    {
        var schedule = SeedSchedule(true, (ScheduleDayType.Weekday, "07:00", "08:00"), (ScheduleDayType.Weekend, "08:00", "09:00"));
        schedule.BreakUntil = Now.AddMinutes(3);
        schedule.ContinuousReadingSince = Now.AddMinutes(-10);
        schedule.LastReadingSeenAt = Now;
        schedule.UsageDate = new DateOnly(2026, 11, 23);
        schedule.UsageSeconds = 1200;

        var dto = await _sut.SetAsync(ChildId, OwnerId, Request((ScheduleDayType.Weekday, "18:00", "20:00")));

        Assert.Equal(new[] { "Weekday 1 18:00-20:00" }, Describe(dto));
        Assert.Single(_uow.Items<ReadingScheduleWindow>());
        Assert.Null(schedule.BreakUntil);
        Assert.Null(schedule.ContinuousReadingSince);
        Assert.Null(schedule.LastReadingSeenAt);
        Assert.Equal(1200, schedule.UsageSeconds);
        _audit.Verify(audit => audit.LogAsync(OwnerId, "SET_READING_SCHEDULE", nameof(ReadingSchedule), schedule.Id,
            It.IsNotNull<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("7h", "08:00")]
    [InlineData("07:00", "")]
    [InlineData("25:00", "26:00")]
    public async Task Set_BadWindowTime_IsRejectedWithoutSaving(string start, string end)
    {
        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SetAsync(ChildId, OwnerId, Request((ScheduleDayType.Weekday, start, end))));

        Assert.Equal(ReadingSchedulePolicy.InvalidTimeMessage, error.Message);
        Assert.Empty(_uow.Items<ReadingSchedule>());
    }

    [Fact]
    public async Task Set_BadBedtimeFormat_IsRejected()
    {
        var request = Request();
        request.BedtimeWeekday = "8:30pm";

        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.SetAsync(ChildId, OwnerId, request));

        Assert.Equal(ReadingSchedulePolicy.InvalidTimeMessage, error.Message);
    }

    [Fact]
    public async Task Set_RuleViolation_UsesPolicyMessage()
    {
        var request = Request();
        request.ScreenLimitWeekdayMinutes = 5;

        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.SetAsync(ChildId, OwnerId, request));

        Assert.Equal(ReadingSchedulePolicy.ScreenLimitMessage, error.Message);
        Assert.Empty(_uow.Items<ReadingSchedule>());
    }

    [Fact]
    public async Task Set_NotOwner_WritesNothing()
    {
        _guard.Setup(guard => guard.EnsureOwnerAsync(ChildId, 99, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Bạn không có quyền với hồ sơ trẻ này."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.SetAsync(ChildId, 99, Request()));
        Assert.Empty(_uow.Items<ReadingSchedule>());
    }

    // ---------- IReadingTimeGate ----------

    [Fact]
    public async Task EnsureCanStart_NoSchedule_Passes() =>
        await _sut.EnsureCanStartAsync(ChildId, childFacing: true);

    [Fact]
    public async Task EnsureCanStart_InsideWindow_Passes()
    {
        SeedSchedule(false, (ScheduleDayType.Weekday, "07:00", "20:30"));

        await _sut.EnsureCanStartAsync(ChildId, childFacing: true);
    }

    [Fact]
    public async Task EnsureCanStart_OutsideWindow_UsesAudienceMessage()
    {
        SeedSchedule(false, (ScheduleDayType.Weekday, "18:00", "20:00"));

        var child = await Assert.ThrowsAsync<ForbiddenException>(() => _sut.EnsureCanStartAsync(ChildId, childFacing: true));
        var parent = await Assert.ThrowsAsync<ForbiddenException>(() => _sut.EnsureCanStartAsync(ChildId, childFacing: false));

        Assert.Equal(ReadingSchedulePolicy.OutsideErrorCode, child.ErrorCode);
        Assert.Equal(ReadingSchedulePolicy.ChildOutsideMessage, child.Message);
        Assert.Equal("Ngoài khung giờ đọc của bé (khung kế tiếp: Thứ 2 18:00).", parent.Message);
    }

    [Fact]
    public async Task EnsureCanStart_DuringBreak_ThrowsBreakCode()
    {
        var schedule = SeedSchedule(true, (ScheduleDayType.Weekday, "07:00", "20:30"));
        schedule.BreakUntil = Now.AddMinutes(3);

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => _sut.EnsureCanStartAsync(ChildId, childFacing: true));

        Assert.Equal(ReadingSchedulePolicy.BreakErrorCode, error.ErrorCode);
        Assert.Equal("Bé nghỉ mắt một chút nhé, còn 3 phút.", error.Message);
    }

    [Fact]
    public async Task EnsureCanStart_AfterBedtime_ThrowsBedtimeCode()
    {
        var schedule = SeedSchedule(false);
        schedule.BedtimeWeekday = T("20:30");
        _clock.Advance(TimeSpan.FromHours(11)); // 21:00 giờ Việt Nam

        var child = await Assert.ThrowsAsync<ForbiddenException>(() => _sut.EnsureCanStartAsync(ChildId, childFacing: true));
        var parent = await Assert.ThrowsAsync<ForbiddenException>(() => _sut.EnsureCanStartAsync(ChildId, childFacing: false));

        Assert.Equal(ReadingSchedulePolicy.BedtimeErrorCode, child.ErrorCode);
        Assert.Equal(ReadingSchedulePolicy.ChildBedtimeMessage, child.Message);
        Assert.Equal("Đã qua giờ đi ngủ của bé (20:30).", parent.Message);
    }

    [Fact]
    public async Task EnsureCanStart_ScreenUsedUp_ThrowsScreenLimitCode()
    {
        var schedule = SeedSchedule(false);
        schedule.ScreenLimitEnabled = true;
        schedule.ScreenLimitWeekdayMinutes = 45;
        schedule.UsageDate = new DateOnly(2026, 11, 23);
        schedule.UsageSeconds = 45 * 60;

        var child = await Assert.ThrowsAsync<ForbiddenException>(() => _sut.EnsureCanStartAsync(ChildId, childFacing: true));
        var parent = await Assert.ThrowsAsync<ForbiddenException>(() => _sut.EnsureCanStartAsync(ChildId, childFacing: false));

        Assert.Equal(ReadingSchedulePolicy.ScreenLimitErrorCode, child.ErrorCode);
        Assert.Equal(ReadingSchedulePolicy.ChildScreenLimitMessage, child.Message);
        Assert.Equal("Bé đã dùng hết 45 phút hôm nay.", parent.Message);
    }

    [Fact]
    public async Task GetStatus_InsideWindow_ReturnsWindowEnd()
    {
        SeedSchedule(false, (ScheduleDayType.Weekday, "07:00", "20:30"));

        var status = await _sut.GetStatusAsync(ChildId);

        Assert.Equal("Allowed", status.State);
        Assert.Equal("Window", status.EndReason);
        Assert.Equal(Now.AddHours(10).AddMinutes(30), status.EndsAt);
        Assert.Null(status.Message);
    }

    [Fact]
    public async Task TrackReading_TwentyMinutesStraight_ReturnsBreakWithActivity()
    {
        SeedSchedule(true, (ScheduleDayType.Weekday, "07:00", "20:30"));
        var started = Now;
        var status = await _sut.TrackReadingAsync(ChildId, started);
        for (var minute = 1; minute <= 20; minute++)
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            status = await _sut.TrackReadingAsync(ChildId, started);
        }

        Assert.Equal("Break", status.State);
        Assert.Equal(Now.AddMinutes(25), status.BreakUntil);
        Assert.NotNull(status.BreakActivity);
        Assert.StartsWith("Bé nghỉ mắt một chút nhé, còn 5 phút.", status.Message);
    }

    [Fact]
    public async Task TrackReading_JustAfterWindowEnd_ReturnsGrace()
    {
        SeedSchedule(false, (ScheduleDayType.Weekday, "07:00", "10:00"));

        var status = await _sut.TrackReadingAsync(ChildId, Now.AddMinutes(-30));

        Assert.Equal("Grace", status.State);
        Assert.Equal("Window", status.EndReason);
        Assert.Equal(Now.AddMinutes(10), status.GraceEndsAt);
        Assert.Equal(ReadingSchedulePolicy.GraceMessage, status.Message);
    }

    // ---------- IScreenTimeRecorder ----------

    [Fact]
    public async Task RecordActivity_AddsGapToToday()
    {
        var schedule = SeedSchedule(false);

        await _sut.RecordActivityAsync(ChildId, Now.AddSeconds(-70), Now);

        Assert.Equal(new DateOnly(2026, 11, 23), schedule.UsageDate);
        Assert.Equal(70, schedule.UsageSeconds);
    }

    [Fact]
    public async Task RecordActivity_NoSchedule_DoesNothing()
    {
        await _sut.RecordActivityAsync(ChildId, Now.AddSeconds(-70), Now);

        Assert.Empty(_uow.Items<ReadingSchedule>());
    }
}
