using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.BusinessReports.Services;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation.Services;

/// <summary>
/// Thống kê hàng đợi và SLA cho Moderator (F-19). Chỉ đọc bảng moderation_items và chỉ trả số tổng hợp.
/// </summary>
public class ModerationStatisticsService : IModerationStatisticsService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public ModerationStatisticsService(IUnitOfWork unitOfWork, TimeProvider? clock = null)
    {
        _unitOfWork = unitOfWork;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<ModerationStatisticsDto> GetAsync(
        int moderatorUserId, DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken = default)
    {
        var (from, toExclusive) = ReportPeriod.Resolve(periodStart, periodEnd);
        var now = _clock.GetUtcNow().UtcDateTime;
        var repository = _unitOfWork.Repository<ModerationItem>();

        var open = await repository.FindAsync(
            item => item.Status == ModerationItemStatus.Pending || item.Status == ModerationItemStatus.Claimed,
            cancellationToken: cancellationToken);
        var decided = await repository.FindAsync(
            item => item.DecidedByUserId == moderatorUserId && item.DecidedAt >= from && item.DecidedAt < toExclusive,
            cancellationToken: cancellationToken);
        var created = await repository.FindAsync(
            item => item.CreatedAt >= from && item.CreatedAt < toExclusive
                    && (item.IsResubmission || item.ItemType == ModerationItemType.GrayZoneStory),
            cancellationToken: cancellationToken);

        bool IsOverdue(ModerationItem item) => item.SlaDueAt != null && item.SlaDueAt < now;
        var queueByType = open
            .GroupBy(item => item.ItemType)
            .OrderBy(group => group.Key)
            .Select(group => new ModerationQueueCountDto
            {
                ItemType = group.Key.ToString(),
                Pending = group.Count(item => item.Status == ModerationItemStatus.Pending),
                Claimed = group.Count(item => item.Status == ModerationItemStatus.Claimed),
                Overdue = group.Count(IsOverdue)
            })
            .ToList();
        var decisionsByType = decided
            .GroupBy(item => item.ItemType)
            .OrderBy(group => group.Key)
            .Select(group => new ModerationDecisionCountDto
            {
                ItemType = group.Key.ToString(),
                Approved = group.Count(item => item.Status == ModerationItemStatus.Approved),
                Rejected = group.Count(item => item.Status == ModerationItemStatus.Rejected),
                ChangesRequested = group.Count(item => item.Status == ModerationItemStatus.ChangesRequested)
            })
            .ToList();

        return new ModerationStatisticsDto
        {
            PeriodStart = from,
            PeriodEnd = toExclusive.AddDays(-1),
            AsOfUtc = now,
            Pending = queueByType.Sum(row => row.Pending),
            Claimed = queueByType.Sum(row => row.Claimed),
            Overdue = queueByType.Sum(row => row.Overdue),
            QueueByType = queueByType,
            MyApproved = decisionsByType.Sum(row => row.Approved),
            MyRejected = decisionsByType.Sum(row => row.Rejected),
            MyChangesRequested = decisionsByType.Sum(row => row.ChangesRequested),
            MyDecisionsByType = decisionsByType,
            ResubmissionCount = created.Count(item => item.IsResubmission),
            GrayZoneCount = created.Count(item => item.ItemType == ModerationItemType.GrayZoneStory)
        };
    }
}
