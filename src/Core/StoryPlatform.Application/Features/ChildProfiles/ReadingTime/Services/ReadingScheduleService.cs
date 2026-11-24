using System.Globalization;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.ReadingTime.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.ReadingTime.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.ReadingTime.Services;

public class ReadingScheduleService : IReadingScheduleService, IReadingTimeGate, IScreenTimeRecorder
{
    private const string TimeFormat = "HH:mm";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildProfileAccessGuard _accessGuard;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly TimeProvider _clock;
    private readonly IReadingHabitService? _habit;

    public ReadingScheduleService(
        IUnitOfWork unitOfWork,
        IChildProfileAccessGuard accessGuard,
        IAuditLogWriter auditLogWriter,
        TimeProvider clock,
        IReadingHabitService? habit = null)
    {
        _unitOfWork = unitOfWork;
        _accessGuard = accessGuard;
        _auditLogWriter = auditLogWriter;
        _clock = clock;
        _habit = habit;
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    public async Task<ReadingScheduleDto> GetAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureOwnerAsync(childProfileId, currentUserId, cancellationToken);
        var (schedule, windows) = await LoadAsync(childProfileId, cancellationToken);
        if (schedule != null)
        {
            var dto = ToDto(childProfileId, schedule, windows, isSuggested: false);
            dto.Habit = await HabitProgressAsync(childProfileId, schedule, cancellationToken);
            return dto;
        }

        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken)
                    ?? throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        return Suggestion(childProfileId, child.AgeBand);
    }

    public async Task<ReadingScheduleDto> SetAsync(
        int childProfileId, int currentUserId, SetReadingScheduleRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureOwnerAsync(childProfileId, currentUserId, cancellationToken);
        var windowsInput = ParseWindows(request.Windows ?? []);
        var bedtimeWeekday = ParseOptionalTime(request.BedtimeWeekday);
        var bedtimeWeekend = ParseOptionalTime(request.BedtimeWeekend);
        var reminderWeekday = ParseOptionalTime(request.ReadingReminderWeekday);
        var reminderWeekend = ParseOptionalTime(request.ReadingReminderWeekend);
        var error = ReadingSchedulePolicy.Validate(new ScheduleSettingsInput(
            request.BreakEnabled, request.BreakAfterMinutes, request.BreakDurationMinutes,
            request.BreakRemindEyes, request.BreakRemindWater, request.BreakRemindMove,
            request.ScreenLimitWeekdayMinutes, request.ScreenLimitWeekendMinutes,
            bedtimeWeekday, bedtimeWeekend, windowsInput,
            reminderWeekday, reminderWeekend, request.WeeklyGoalDays));
        if (error != null)
        {
            throw new BadRequestException(error);
        }

        var scheduleRepo = _unitOfWork.Repository<ReadingSchedule>();
        var windowRepo = _unitOfWork.Repository<ReadingScheduleWindow>();
        var (schedule, existing) = await LoadAsync(childProfileId, cancellationToken);
        var beforeState = schedule == null ? null : Snapshot(schedule, existing);
        if (schedule == null)
        {
            schedule = new ReadingSchedule { ChildProfileId = childProfileId };
            await scheduleRepo.AddAsync(schedule, cancellationToken);
        }
        else
        {
            schedule.UpdatedAt = UtcNow;
            scheduleRepo.Update(schedule);
            if (existing.Count > 0)
            {
                windowRepo.DeleteRange(existing);
            }
        }

        schedule.Enabled = request.Enabled;
        schedule.BedtimeWeekday = bedtimeWeekday;
        schedule.BedtimeWeekend = bedtimeWeekend;
        schedule.BreakEnabled = request.BreakEnabled;
        schedule.BreakAfterMinutes = request.BreakAfterMinutes;
        schedule.BreakDurationMinutes = request.BreakDurationMinutes;
        schedule.BreakRemindEyes = request.BreakRemindEyes;
        schedule.BreakRemindWater = request.BreakRemindWater;
        schedule.BreakRemindMove = request.BreakRemindMove;
        schedule.ScreenLimitEnabled = request.ScreenLimitEnabled;
        schedule.ScreenLimitWeekdayMinutes = request.ScreenLimitWeekdayMinutes;
        schedule.ScreenLimitWeekendMinutes = request.ScreenLimitWeekendMinutes;
        schedule.HabitEnabled = request.HabitEnabled;
        schedule.ReadingReminderWeekday = reminderWeekday;
        schedule.ReadingReminderWeekend = reminderWeekend;
        schedule.WeeklyGoalDays = request.WeeklyGoalDays;
        // Trạng thái nghỉ mắt bắt đầu lại; thời gian màn hình đã dùng hôm nay được giữ.
        schedule.ContinuousReadingSince = null;
        schedule.LastReadingSeenAt = null;
        schedule.BreakUntil = null;

        var windows = new List<ReadingScheduleWindow>();
        foreach (var group in windowsInput.GroupBy(window => window.DayType))
        {
            var slot = 1;
            foreach (var input in group.OrderBy(window => window.Start))
            {
                var window = new ReadingScheduleWindow
                {
                    ReadingSchedule = schedule,
                    ReadingScheduleId = schedule.Id,
                    DayType = input.DayType,
                    Slot = slot++,
                    StartTime = input.Start,
                    EndTime = input.End
                };
                await windowRepo.AddAsync(window, cancellationToken);
                windows.Add(window);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _auditLogWriter.LogAsync(
            currentUserId, "SET_READING_SCHEDULE", nameof(ReadingSchedule), schedule.Id,
            beforeState, Snapshot(schedule, windows), cancellationToken);

        var result = ToDto(childProfileId, schedule, windows, isSuggested: false);
        result.Habit = await HabitProgressAsync(childProfileId, schedule, cancellationToken);
        return result;
    }

    public async Task EnsureCanStartAsync(
        int childProfileId, bool childFacing, CancellationToken cancellationToken = default)
    {
        var (schedule, windows) = await LoadAsync(childProfileId, cancellationToken);
        var now = UtcNow;
        var status = ReadingSchedulePolicy.Evaluate(schedule, windows, now);
        switch (status.State)
        {
            case ReadingTimeState.Break:
                throw new ForbiddenException(
                    ReadingSchedulePolicy.BreakMessage(status.BreakUntil ?? now, now),
                    ReadingSchedulePolicy.BreakErrorCode);
            case ReadingTimeState.Bedtime:
                throw new ForbiddenException(
                    childFacing
                        ? ReadingSchedulePolicy.ChildBedtimeMessage
                        : ReadingSchedulePolicy.ParentBedtimeMessage(status.BedtimeAt),
                    ReadingSchedulePolicy.BedtimeErrorCode);
            case ReadingTimeState.ScreenLimitReached:
                throw new ForbiddenException(
                    childFacing
                        ? ReadingSchedulePolicy.ChildScreenLimitMessage
                        : ReadingSchedulePolicy.ParentScreenLimitMessage(status.ScreenMinutesLimit),
                    ReadingSchedulePolicy.ScreenLimitErrorCode);
            case ReadingTimeState.Outside:
                throw new ForbiddenException(
                    childFacing
                        ? ReadingSchedulePolicy.ChildOutsideMessage
                        : ReadingSchedulePolicy.ParentOutsideMessage(status.NextAllowedAt),
                    ReadingSchedulePolicy.OutsideErrorCode);
        }
    }

    public async Task<ReadingTimeStatusDto> GetStatusAsync(
        int childProfileId, CancellationToken cancellationToken = default)
    {
        var (schedule, windows) = await LoadAsync(childProfileId, cancellationToken);
        var now = UtcNow;
        return ToStatusDto(ReadingSchedulePolicy.Evaluate(schedule, windows, now), now);
    }

    public async Task<ReadingTimeStatusDto> TrackReadingAsync(
        int childProfileId, DateTime readingStartedAtUtc, CancellationToken cancellationToken = default)
    {
        var (schedule, windows) = await LoadAsync(childProfileId, cancellationToken);
        var now = UtcNow;
        if (schedule != null && ReadingSchedulePolicy.TrackReading(schedule, now))
        {
            schedule.UpdatedAt = now;
            _unitOfWork.Repository<ReadingSchedule>().Update(schedule);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return ToStatusDto(ReadingSchedulePolicy.Evaluate(schedule, windows, now, readingStartedAtUtc), now);
    }

    public async Task RecordActivityAsync(
        int childProfileId, DateTime previousActivityUtc, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var repository = _unitOfWork.Repository<ReadingSchedule>();
        var schedule = await repository.FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        if (schedule != null && ReadingSchedulePolicy.AddScreenTime(schedule, previousActivityUtc, nowUtc))
        {
            repository.Update(schedule);
        }
    }

    private async Task<ReadingHabitProgressDto?> HabitProgressAsync(
        int childProfileId, ReadingSchedule schedule, CancellationToken cancellationToken) =>
        _habit == null ? null : await _habit.GetProgressAsync(childProfileId, schedule.WeeklyGoalDays, cancellationToken);

    private async Task<(ReadingSchedule? Schedule, IReadOnlyList<ReadingScheduleWindow> Windows)> LoadAsync(
        int childProfileId, CancellationToken cancellationToken)
    {
        var schedule = await _unitOfWork.Repository<ReadingSchedule>().FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        if (schedule == null)
        {
            return (null, Array.Empty<ReadingScheduleWindow>());
        }

        var windows = await _unitOfWork.Repository<ReadingScheduleWindow>().FindAsync(
            value => value.ReadingScheduleId == schedule.Id, cancellationToken: cancellationToken);
        return (schedule, windows);
    }

    private static List<ScheduleWindowInput> ParseWindows(IEnumerable<SetReadingScheduleWindowRequestDto> windows)
    {
        var parsed = new List<ScheduleWindowInput>();
        foreach (var window in windows)
        {
            if (!TryParseTime(window.StartTime, out var start) || !TryParseTime(window.EndTime, out var end))
            {
                throw new BadRequestException(ReadingSchedulePolicy.InvalidTimeMessage);
            }

            parsed.Add(new ScheduleWindowInput(window.DayType, start, end));
        }

        return parsed;
    }

    private static TimeOnly? ParseOptionalTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return TryParseTime(value, out var time)
            ? time
            : throw new BadRequestException(ReadingSchedulePolicy.InvalidTimeMessage);
    }

    private static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static string Format(TimeOnly time) => time.ToString(TimeFormat, CultureInfo.InvariantCulture);

    private static string? FormatOptional(TimeOnly? time) => time is { } value ? Format(value) : null;

    private static ReadingScheduleDto Suggestion(int childProfileId, AgeBand ageBand)
    {
        var young = ageBand == AgeBand.Age_6_8;
        var bedtime = young ? new TimeOnly(20, 30) : new TimeOnly(21, 0);
        var schedule = new ReadingSchedule
        {
            ChildProfileId = childProfileId,
            Enabled = false,
            BedtimeWeekday = bedtime,
            BedtimeWeekend = bedtime,
            BreakEnabled = true,
            BreakAfterMinutes = young ? 20 : 30,
            BreakDurationMinutes = 5,
            BreakRemindEyes = true,
            BreakRemindWater = true,
            BreakRemindMove = true,
            ScreenLimitEnabled = true,
            ScreenLimitWeekdayMinutes = young ? 45 : 60,
            ScreenLimitWeekendMinutes = young ? 90 : 120,
            HabitEnabled = false,
            ReadingReminderWeekday = new TimeOnly(19, 0),
            ReadingReminderWeekend = new TimeOnly(9, 0),
            WeeklyGoalDays = young ? 3 : 4
        };
        var windows = young
            ? new[]
            {
                Window(ScheduleDayType.Weekday, 1, new TimeOnly(18, 0), new TimeOnly(20, 0)),
                Window(ScheduleDayType.Weekend, 1, new TimeOnly(8, 0), new TimeOnly(11, 0)),
                Window(ScheduleDayType.Weekend, 2, new TimeOnly(15, 0), new TimeOnly(20, 0))
            }
            : new[]
            {
                Window(ScheduleDayType.Weekday, 1, new TimeOnly(18, 0), new TimeOnly(20, 30)),
                Window(ScheduleDayType.Weekend, 1, new TimeOnly(8, 0), new TimeOnly(11, 30)),
                Window(ScheduleDayType.Weekend, 2, new TimeOnly(15, 0), new TimeOnly(20, 30))
            };
        return ToDto(childProfileId, schedule, windows, isSuggested: true);
    }

    private static ReadingScheduleWindow Window(ScheduleDayType dayType, int slot, TimeOnly start, TimeOnly end) =>
        new() { DayType = dayType, Slot = slot, StartTime = start, EndTime = end };

    private static ReadingScheduleDto ToDto(
        int childProfileId, ReadingSchedule schedule, IEnumerable<ReadingScheduleWindow> windows, bool isSuggested) => new()
    {
        ChildProfileId = childProfileId,
        Enabled = schedule.Enabled,
        BedtimeWeekday = FormatOptional(schedule.BedtimeWeekday),
        BedtimeWeekend = FormatOptional(schedule.BedtimeWeekend),
        BreakEnabled = schedule.BreakEnabled,
        BreakAfterMinutes = schedule.BreakAfterMinutes,
        BreakDurationMinutes = schedule.BreakDurationMinutes,
        BreakRemindEyes = schedule.BreakRemindEyes,
        BreakRemindWater = schedule.BreakRemindWater,
        BreakRemindMove = schedule.BreakRemindMove,
        ScreenLimitEnabled = schedule.ScreenLimitEnabled,
        ScreenLimitWeekdayMinutes = schedule.ScreenLimitWeekdayMinutes,
        ScreenLimitWeekendMinutes = schedule.ScreenLimitWeekendMinutes,
        HabitEnabled = schedule.HabitEnabled,
        ReadingReminderWeekday = FormatOptional(schedule.ReadingReminderWeekday),
        ReadingReminderWeekend = FormatOptional(schedule.ReadingReminderWeekend),
        WeeklyGoalDays = schedule.WeeklyGoalDays,
        IsSuggested = isSuggested,
        Windows = windows
            .OrderBy(window => window.DayType)
            .ThenBy(window => window.Slot)
            .Select(window => new ReadingScheduleWindowDto
            {
                DayType = window.DayType.ToString(),
                Slot = window.Slot,
                StartTime = Format(window.StartTime),
                EndTime = Format(window.EndTime)
            })
            .ToList()
    };

    private static ReadingTimeStatusDto ToStatusDto(ReadingTimeStatus status, DateTime nowUtc) => new()
    {
        State = status.State.ToString(),
        EndReason = status.EndReason?.ToString(),
        EndsAt = status.EndsAt,
        GraceEndsAt = status.GraceEndsAt,
        BreakUntil = status.BreakUntil,
        BreakActivity = status.BreakActivity?.ToString(),
        NextAllowedAt = status.NextAllowedAt,
        BedtimeAt = status.BedtimeAt,
        ScreenMinutesUsed = status.ScreenMinutesUsed,
        ScreenMinutesLimit = status.ScreenMinutesLimit,
        Message = ReadingSchedulePolicy.MessageFor(status, nowUtc)
    };

    private static object Snapshot(ReadingSchedule schedule, IEnumerable<ReadingScheduleWindow> windows) => new
    {
        schedule.ChildProfileId,
        schedule.Enabled,
        BedtimeWeekday = FormatOptional(schedule.BedtimeWeekday),
        BedtimeWeekend = FormatOptional(schedule.BedtimeWeekend),
        schedule.BreakEnabled,
        schedule.BreakAfterMinutes,
        schedule.BreakDurationMinutes,
        schedule.BreakRemindEyes,
        schedule.BreakRemindWater,
        schedule.BreakRemindMove,
        schedule.ScreenLimitEnabled,
        schedule.ScreenLimitWeekdayMinutes,
        schedule.ScreenLimitWeekendMinutes,
        schedule.HabitEnabled,
        ReadingReminderWeekday = FormatOptional(schedule.ReadingReminderWeekday),
        ReadingReminderWeekend = FormatOptional(schedule.ReadingReminderWeekend),
        schedule.WeeklyGoalDays,
        Windows = windows
            .OrderBy(window => window.DayType)
            .ThenBy(window => window.StartTime)
            .Select(window => $"{window.DayType} {Format(window.StartTime)}-{Format(window.EndTime)}")
            .ToList()
    };
}
