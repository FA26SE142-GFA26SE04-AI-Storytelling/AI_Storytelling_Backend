using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Features.ChildProfiles.ReadingTime.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.ReadingTime.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.ReadingTime.Services;

public class ReadingHabitService : IReadingHabitService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public ReadingHabitService(IUnitOfWork unitOfWork, TimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    public async Task<ReadingHabitProgressDto> GetProgressAsync(
        int childProfileId, int? weeklyGoalDays, CancellationToken cancellationToken = default)
    {
        var daysRead = (await ReadDaysThisWeekAsync(childProfileId, cancellationToken)).Count;
        return new ReadingHabitProgressDto
        {
            StreakDays = await LiveStreakAsync(childProfileId, cancellationToken),
            DaysReadThisWeek = daysRead,
            GoalMetThisWeek = weeklyGoalDays is { } goal && daysRead >= goal
        };
    }

    public async Task<ChildHabitDto> GetChildHabitAsync(
        int childProfileId, CancellationToken cancellationToken = default)
    {
        var now = UtcNow;
        var daysRead = await ReadDaysThisWeekAsync(childProfileId, cancellationToken);
        var schedule = await _unitOfWork.Repository<ReadingSchedule>().FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        var habit = new ChildHabitDto
        {
            StreakDays = await LiveStreakAsync(childProfileId, cancellationToken),
            DaysReadThisWeek = daysRead.Count
        };
        if (schedule is not { HabitEnabled: true })
        {
            return habit;
        }

        var windows = await _unitOfWork.Repository<ReadingScheduleWindow>().FindAsync(
            value => value.ReadingScheduleId == schedule.Id, cancellationToken: cancellationToken);
        var readToday = daysRead.Contains(VietnamTime.LocalDate(now));

        habit.Enabled = true;
        habit.WeeklyGoalDays = schedule.WeeklyGoalDays;
        habit.GoalMetThisWeek = schedule.WeeklyGoalDays is { } goal && daysRead.Count >= goal;
        habit.Reminders = ReadingSchedulePolicy.BuildReminders(schedule, now, readToday)
            .Select(at => new HabitReminderDto { At = at, Message = ReadingSchedulePolicy.ReadingReminderMessage })
            .ToList();
        habit.QuietPeriods = ReadingSchedulePolicy.BuildQuietPeriods(schedule, windows, now)
            .Select(period => new HabitQuietPeriodDto { From = period.FromUtc, To = period.ToUtc })
            .ToList();
        return habit;
    }

    // Các ngày Việt Nam trong tuần này (từ Thứ 2) bé hoàn thành ≥ 1 truyện.
    private async Task<HashSet<DateTime>> ReadDaysThisWeekAsync(int childProfileId, CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var weekStart = VietnamTime.StartOfWeekUtc(now);
        var nextWeek = VietnamTime.StartOfNextWeekUtc(now);
        var sessions = await _unitOfWork.Repository<ReadingSession>().FindAsync(
            value => value.ChildProfileId == childProfileId
                     && value.Status == SessionStatus.Completed
                     && value.CompletedAt != null
                     && value.CompletedAt >= weekStart
                     && value.CompletedAt < nextWeek,
            cancellationToken: cancellationToken);
        return sessions.Select(value => VietnamTime.LocalDate(value.CompletedAt!.Value)).ToHashSet();
    }

    private async Task<int> LiveStreakAsync(int childProfileId, CancellationToken cancellationToken)
    {
        var achievement = await _unitOfWork.Repository<Achievement>().FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId && !value.IsDeleted,
            cancellationToken: cancellationToken);
        return RewardPolicy.LiveStreakDays(achievement, RewardPolicy.LocalToday(_clock));
    }
}
