using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

public class ChildRoadmapProgressRecorder : IChildRoadmapProgressRecorder
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildRoadmapAdvancer _advancer;
    private readonly IChildRoadmapNotifier _notifier;
    private readonly IAuditLogWriter _auditLogWriter;

    public ChildRoadmapProgressRecorder(
        IUnitOfWork unitOfWork,
        IChildRoadmapAdvancer advancer,
        IChildRoadmapNotifier notifier,
        IAuditLogWriter auditLogWriter)
    {
        _unitOfWork = unitOfWork;
        _advancer = advancer;
        _notifier = notifier;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<ChildRoadmapItem?> RecordSessionCompletedAsync(
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
        var candidates = await repository.FindAsync(
            item => item.ChildProfileId == childId
                    && item.StoryId == storyId
                    && (item.Status == ChildRoadmapItemStatus.Open || item.Status == ChildRoadmapItemStatus.Waiting),
            cancellationToken: cancellationToken);
        var match = candidates.OrderBy(item => item.Status == ChildRoadmapItemStatus.Open ? 0 : 1)
            .ThenBy(item => item.Position).FirstOrDefault();
        if (match == null)
        {
            return null;
        }

        var step = await repository.GetByIdAsync(match.Id, cancellationToken);
        if (step == null || step.IsDeleted)
        {
            return null;
        }

        var wasOpen = step.Status == ChildRoadmapItemStatus.Open;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            step.Status = ChildRoadmapItemStatus.Completed;
            step.CompletedAt = DateTime.UtcNow;
            repository.Update(step);
            session.ChildRoadmapItemId = step.Id;
            _unitOfWork.Repository<ReadingSession>().Update(session);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (wasOpen)
            {
                await _advancer.AdvanceAsync(childId, cancellationToken);
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
            var holdMode = await _unitOfWork.Repository<InterventionCase>().ExistsAsync(
                holdCase => holdCase.ChildProfileId == childId
                            && !holdCase.IsDeleted
                            && holdCase.Status != InterventionStatus.ResolvedUnlocked,
                cancellationToken);
            if (remaining.Count == 0 && !holdMode)
            {
                await _notifier.NotifyRoadmapCompletedAsync(childId, cancellationToken);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return step;
    }
}
