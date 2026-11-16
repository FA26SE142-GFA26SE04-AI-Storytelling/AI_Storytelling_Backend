using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

public partial class ChildRoadmapService
{
    public async Task<ChildRoadmapDto> RemoveAsync(
        int actorUserId, int childProfileId, int itemId, CancellationToken cancellationToken = default)
    {
        await _guard.EnsureOwnerAsync(childProfileId, actorUserId, cancellationToken);
        var repository = _unitOfWork.Repository<ChildRoadmapItem>();
        var item = await repository.GetByIdAsync(itemId, cancellationToken);
        if (item == null || item.IsDeleted || item.ChildProfileId != childProfileId)
        {
            throw new NotFoundException("Bước trong lộ trình", itemId);
        }

        if (item.Status is not (ChildRoadmapItemStatus.Waiting or ChildRoadmapItemStatus.Open))
        {
            throw new ConflictException("Truyện này đã đọc xong, đã bỏ qua hoặc đã gỡ nên không gỡ được nữa.");
        }

        var wasOpen = item.Status == ChildRoadmapItemStatus.Open;
        var before = item.Status;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(-childProfileId, cancellationToken);
            MarkRemoved(item, actorUserId, DateTime.UtcNow);
            repository.Update(item);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await RoadmapStages.RenumberWaitingAsync(_unitOfWork, childProfileId, cancellationToken);

            if (wasOpen)
            {
                await _advancer.AdvanceAsync(childProfileId, cancellationToken);
            }

            await _auditLogWriter.LogAsync(
                actorUserId, "ChildRoadmapStoryRemoved", nameof(ChildRoadmapItem), item.Id,
                beforeState: new { status = before.ToString() },
                afterState: new { status = ChildRoadmapItemStatus.Removed.ToString(), childProfileId, storyId = item.StoryId },
                cancellationToken: cancellationToken);
            await _notifier.NotifyOtherSupervisorsAsync(
                childProfileId, actorUserId, NotificationType.RoadmapStoryRemoved,
                new { childProfileId, storyId = item.StoryId }, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return await BuildRoadmapAsync(childProfileId, cancellationToken);
    }

    public async Task<ChildRoadmapDto> ReorderAsync(
        int actorUserId, int childProfileId, ReorderRoadmapRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await _guard.EnsureOwnerAsync(childProfileId, actorUserId, cancellationToken);
        var repository = _unitOfWork.Repository<ChildRoadmapItem>();
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(-childProfileId, cancellationToken);
            var live = await repository.FindAsync(
                item => item.ChildProfileId == childProfileId
                        && (item.Status == ChildRoadmapItemStatus.Waiting || item.Status == ChildRoadmapItemStatus.Open
                            || item.Status == ChildRoadmapItemStatus.Completed),
                cancellationToken: cancellationToken);
            var waiting = live.Where(item => item.Status == ChildRoadmapItemStatus.Waiting).ToList();
            var stages = request.Stages ?? new List<List<int>>();
            var requested = stages.SelectMany(stage => stage ?? new List<int>()).ToList();
            if (stages.Any(stage => stage == null || stage.Count == 0)
                || requested.Count != requested.Distinct().Count()
                || requested.Count != waiting.Count
                || !waiting.Select(item => item.Id).OrderBy(id => id).SequenceEqual(requested.OrderBy(id => id)))
            {
                throw new BadRequestException(
                    "Danh sách phải gồm đúng các truyện đang chờ, chia theo chặng, mỗi truyện một lần; truyện đang mở không kéo thả được.");
            }

            var current = RoadmapStages.Current(live);
            for (var index = 0; index < stages.Count; index++)
            {
                foreach (var id in stages[index])
                {
                    var tracked = await repository.GetByIdAsync(id, cancellationToken);
                    tracked!.Position = current + index + 1;
                    repository.Update(tracked);
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _auditLogWriter.LogAsync(
                actorUserId, "ChildRoadmapReordered", nameof(ChildProfile), childProfileId,
                beforeState: null, afterState: new { stages }, cancellationToken: cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return await BuildRoadmapAsync(childProfileId, cancellationToken);
    }

    public async Task<ChildRoadmapDto> ClearAsync(
        int actorUserId, int childProfileId, CancellationToken cancellationToken = default)
    {
        await _guard.EnsureOwnerAsync(childProfileId, actorUserId, cancellationToken);
        var repository = _unitOfWork.Repository<ChildRoadmapItem>();
        var active = await repository.FindAsync(
            item => item.ChildProfileId == childProfileId
                    && (item.Status == ChildRoadmapItemStatus.Waiting || item.Status == ChildRoadmapItemStatus.Open),
            cancellationToken: cancellationToken);
        if (active.Count == 0)
        {
            return await BuildRoadmapAsync(childProfileId, cancellationToken);
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(-childProfileId, cancellationToken);
            var now = DateTime.UtcNow;
            foreach (var candidate in active)
            {
                var tracked = await repository.GetByIdAsync(candidate.Id, cancellationToken);
                MarkRemoved(tracked!, actorUserId, now);
                repository.Update(tracked!);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _auditLogWriter.LogAsync(
                actorUserId, "ChildRoadmapCleared", nameof(ChildProfile), childProfileId,
                beforeState: new { removed = active.Select(item => item.Id).ToList() },
                afterState: new { status = ChildRoadmapItemStatus.Removed.ToString() },
                cancellationToken: cancellationToken);
            await _notifier.NotifyOtherSupervisorsAsync(
                childProfileId, actorUserId, NotificationType.RoadmapStoryRemoved,
                new { childProfileId, cleared = true, count = active.Count }, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return await BuildRoadmapAsync(childProfileId, cancellationToken);
    }

    private static void MarkRemoved(ChildRoadmapItem item, int actorUserId, DateTime now)
    {
        item.Status = ChildRoadmapItemStatus.Removed;
        item.RemovedAt = now;
        item.RemovedByUserId = actorUserId;
    }
}
