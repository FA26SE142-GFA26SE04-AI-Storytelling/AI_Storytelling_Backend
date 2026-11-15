using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

public class ChildRoadmapProgressRecorder : IChildRoadmapProgressRecorder
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildRoadmapAdvancer _advancer;
    private readonly IChildRoadmapNotifier _notifier;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IChildRewardService _rewards;

    public ChildRoadmapProgressRecorder(
        IUnitOfWork unitOfWork,
        IChildRoadmapAdvancer advancer,
        IChildRoadmapNotifier notifier,
        IAuditLogWriter auditLogWriter,
        IChildRewardService rewards)
    {
        _unitOfWork = unitOfWork;
        _advancer = advancer;
        _notifier = notifier;
        _auditLogWriter = auditLogWriter;
        _rewards = rewards;
    }

    public async Task<RoadmapCompletionResult?> RecordSessionCompletedAsync(
        int readingSessionId, bool isRetake, CancellationToken cancellationToken = default)
    {
        if (isRetake)
        {
            return null;
        }

        var session = await _unitOfWork.Repository<ReadingSession>().GetByIdAsync(readingSessionId, cancellationToken);
        if (session == null || session.IsDeleted || session.Status != SessionStatus.Completed)
        {
            return null;
        }

        var childId = session.ChildProfileId;
        var storyId = session.StoryId;
        var repository = _unitOfWork.Repository<ChildRoadmapItem>();
        var hasCandidate = await repository.ExistsAsync(
            item => item.ChildProfileId == childId
                    && item.StoryId == storyId
                    && (item.Status == ChildRoadmapItemStatus.Open || item.Status == ChildRoadmapItemStatus.Waiting),
            cancellationToken);
        if (!hasCandidate)
        {
            return null;
        }

        IReadOnlyList<string> newBadges = Array.Empty<string>();
        ChildRoadmapItem? step;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Khoá theo bé: các ghi lộ trình của cùng một bé phải tuần tự để giữ bất biến chặng.
            await _unitOfWork.AcquireTransactionLockAsync(-childId, cancellationToken);

            var candidates = await repository.FindAsync(
                item => item.ChildProfileId == childId
                        && item.StoryId == storyId
                        && (item.Status == ChildRoadmapItemStatus.Open || item.Status == ChildRoadmapItemStatus.Waiting),
                cancellationToken: cancellationToken);
            var match = candidates.OrderBy(item => item.Status == ChildRoadmapItemStatus.Open ? 0 : 1)
                .ThenBy(item => item.Position).FirstOrDefault();
            step = match == null ? null : await repository.GetByIdAsync(match.Id, cancellationToken);
            if (step == null || step.IsDeleted)
            {
                // Giao dịch thuộc về người gọi (BeginTransaction không làm gì khi đã có giao dịch): không rollback ở đây.
                return null;
            }

            var live = await repository.FindAsync(
                item => item.ChildProfileId == childId
                        && (item.Status == ChildRoadmapItemStatus.Open || item.Status == ChildRoadmapItemStatus.Completed),
                cancellationToken: cancellationToken);
            var currentStage = Math.Max(RoadmapStages.Current(live), 1);
            var wasOpen = step.Status == ChildRoadmapItemStatus.Open;

            step.Status = ChildRoadmapItemStatus.Completed;
            step.CompletedAt = DateTime.UtcNow;
            if (!wasOpen)
            {
                // Đọc sớm truyện đang chờ: tính vào chặng hiện tại để không có truyện đã đọc trong chặng tương lai.
                step.Position = currentStage;
            }

            repository.Update(step);
            session.ChildRoadmapItemId = step.Id;
            _unitOfWork.Repository<ReadingSession>().Update(session);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (wasOpen)
            {
                await _advancer.AdvanceAsync(childId, cancellationToken);
            }
            else
            {
                await RoadmapStages.RenumberWaitingAsync(_unitOfWork, childId, cancellationToken);
            }

            await _auditLogWriter.LogAsync(
                null, "ChildRoadmapStepCompleted", nameof(ChildRoadmapItem), step.Id,
                beforeState: new { status = wasOpen ? "Open" : "Waiting" },
                afterState: new
                {
                    status = ChildRoadmapItemStatus.Completed.ToString(),
                    childProfileId = childId,
                    storyId,
                    readingSessionId
                },
                cancellationToken: cancellationToken);

            var remaining = await repository.FindAsync(
                item => item.ChildProfileId == childId
                        && (item.Status == ChildRoadmapItemStatus.Open || item.Status == ChildRoadmapItemStatus.Waiting),
                cancellationToken: cancellationToken);
            if (remaining.Count == 0)
            {
                await _notifier.NotifyRoadmapCompletedAsync(childId, cancellationToken);
                var finished = await _rewards.OnRoadmapFinishedAsync(session, cancellationToken);
                newBadges = finished.NewBadgeCodes;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return new RoadmapCompletionResult(step!, newBadges);
    }
}
