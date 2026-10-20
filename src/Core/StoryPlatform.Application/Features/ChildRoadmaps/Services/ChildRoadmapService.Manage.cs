using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

public partial class ChildRoadmapService
{
    public async Task<ChildRoadmapDto> RemoveAsync(
        int actorUserId, int childProfileId, int itemId, CancellationToken cancellationToken = default)
    {
        await _guard.EnsurePermissionAsync(childProfileId, actorUserId, Permission.AssignActivity, cancellationToken);
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
            MarkRemoved(item, actorUserId, DateTime.UtcNow);
            repository.Update(item);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await RenumberWaitingAsync(childProfileId, cancellationToken);

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
        await _guard.EnsurePermissionAsync(childProfileId, actorUserId, Permission.AssignActivity, cancellationToken);
        var repository = _unitOfWork.Repository<ChildRoadmapItem>();
        var waiting = await repository.FindAsync(
            item => item.ChildProfileId == childProfileId && item.Status == ChildRoadmapItemStatus.Waiting,
            cancellationToken: cancellationToken);
        var requested = request.ItemIds ?? new List<int>();
        if (requested.Count != requested.Distinct().Count()
            || requested.Count != waiting.Count
            || !waiting.Select(item => item.Id).OrderBy(id => id).SequenceEqual(requested.OrderBy(id => id)))
        {
            throw new BadRequestException(
                "Danh sách phải gồm đúng các truyện đang chờ, mỗi truyện một lần; truyện đang mở không kéo thả được.");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            for (var index = 0; index < requested.Count; index++)
            {
                var tracked = await repository.GetByIdAsync(requested[index], cancellationToken);
                tracked!.Position = index + 1;
                repository.Update(tracked);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _auditLogWriter.LogAsync(
                actorUserId, "ChildRoadmapReordered", nameof(ChildProfile), childProfileId,
                beforeState: null, afterState: new { order = requested }, cancellationToken: cancellationToken);
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
        await _guard.EnsurePermissionAsync(childProfileId, actorUserId, Permission.AssignActivity, cancellationToken);
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

    private async Task RenumberWaitingAsync(int childProfileId, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.Repository<ChildRoadmapItem>();
        var waiting = await repository.FindAsync(
            item => item.ChildProfileId == childProfileId && item.Status == ChildRoadmapItemStatus.Waiting,
            cancellationToken: cancellationToken);
        var position = 1;
        var changed = false;
        foreach (var candidate in waiting.OrderBy(item => item.Position).ThenBy(item => item.Id))
        {
            if (candidate.Position != position)
            {
                var tracked = await repository.GetByIdAsync(candidate.Id, cancellationToken);
                tracked!.Position = position;
                repository.Update(tracked);
                changed = true;
            }

            position += 1;
        }

        if (changed)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
