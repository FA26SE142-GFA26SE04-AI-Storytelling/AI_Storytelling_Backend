using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.RoadmapTemplates.Services;

public partial class RoadmapTemplateService
{
    public async Task<RoadmapSubmitResultDto> SubmitAsync(
        int actorUserId, int roadmapId, CancellationToken cancellationToken = default)
    {
        var actor = await RequireModeratorAsync(actorUserId, cancellationToken);
        var roadmap = await LoadOwnedDraftAsync(actor, roadmapId, cancellationToken);
        var items = (await _unitOfWork.Repository<ReadingRoadmapItem>().FindAsync(
                item => item.ReadingRoadmapId == roadmap.Id, cancellationToken: cancellationToken))
            .OrderBy(item => item.Position).ToList();
        var failing = (await _checker.EvaluateAsync(roadmap, items, cancellationToken))
            .Where(check => !check.Passed).ToList();
        if (failing.Count > 0)
        {
            throw new BadRequestException(
                "Chưa thể gửi duyệt: " + string.Join(" ", failing.Select(check => check.Message)));
        }

        var rid = roadmap.Id;
        var hasOpenItem = await _unitOfWork.Repository<ModerationItem>().ExistsAsync(
            item => item.ItemType == ModerationItemType.RoadmapTemplateReview
                    && item.ReadingRoadmapId == rid
                    && (item.Status == ModerationItemStatus.Pending
                        || item.Status == ModerationItemStatus.Claimed),
            cancellationToken);
        if (hasOpenItem)
        {
            throw new ConflictException("Bộ này đang có yêu cầu duyệt chưa xong.");
        }

        var now = DateTime.UtcNow;
        ModerationItem queued;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            roadmap.Status = RoadmapTemplateStatus.PendingReview;
            roadmap.SubmittedAt = now;
            _unitOfWork.Repository<ReadingRoadmap>().Update(roadmap);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (roadmap.ClonedFromId.HasValue)
            {
                var source = await _unitOfWork.Repository<ReadingRoadmap>()
                    .GetByIdAsync(roadmap.ClonedFromId.Value, cancellationToken);
                if (source != null && !source.IsDeleted && source.Status == RoadmapTemplateStatus.Draft)
                {
                    source.Status = RoadmapTemplateStatus.Archived;
                    source.ArchivedReason = RoadmapArchiveReason.ReplacedByClone;
                    source.ArchivedAt = now;
                    source.ArchivedByUserId = actor.Id;
                    _unitOfWork.Repository<ReadingRoadmap>().Update(source);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    await _auditLogWriter.LogAsync(
                        actor.Id, "RoadmapTemplateArchived", nameof(ReadingRoadmap), source.Id,
                        beforeState: new { status = RoadmapTemplateStatus.Draft.ToString() },
                        afterState: new
                        {
                            status = RoadmapTemplateStatus.Archived.ToString(),
                            reason = RoadmapArchiveReason.ReplacedByClone.ToString(),
                            replacedByRoadmapId = roadmap.Id
                        },
                        cancellationToken: cancellationToken);
                }
            }

            queued = await _queueService.EnqueueAsync(new EnqueueModerationItemCommand
            {
                ItemType = ModerationItemType.RoadmapTemplateReview,
                ReadingRoadmapId = roadmap.Id,
                CreatedByUserId = actor.Id
            }, cancellationToken);

            await _auditLogWriter.LogAsync(
                actor.Id, "RoadmapTemplateSubmitted", nameof(ReadingRoadmap), roadmap.Id,
                beforeState: new { status = RoadmapTemplateStatus.Draft.ToString() },
                afterState: new
                {
                    status = RoadmapTemplateStatus.PendingReview.ToString(),
                    moderationItemId = queued.Id,
                    storyCount = items.Count
                },
                cancellationToken: cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return new RoadmapSubmitResultDto
        {
            RoadmapId = roadmap.Id,
            Status = roadmap.Status.ToString(),
            ItemId = queued.Id
        };
    }

    public async Task<RoadmapSubmitResultDto> WithdrawAsync(
        int actorUserId, int roadmapId, CancellationToken cancellationToken = default)
    {
        var actor = await RequireModeratorAsync(actorUserId, cancellationToken);
        var roadmap = await _unitOfWork.Repository<ReadingRoadmap>().GetByIdAsync(roadmapId, cancellationToken);
        if (roadmap == null || roadmap.IsDeleted)
        {
            throw new NotFoundException("Bộ truyện mẫu", roadmapId);
        }

        if (roadmap.CreatedByUserId != actor.Id)
        {
            throw new ForbiddenException("Chỉ người tạo mới rút được yêu cầu duyệt của bộ này.");
        }

        if (roadmap.Status != RoadmapTemplateStatus.PendingReview)
        {
            throw new ConflictException("Bộ này không ở trạng thái chờ duyệt nên không có yêu cầu để rút.");
        }

        var rid = roadmap.Id;
        var openItems = await _unitOfWork.Repository<ModerationItem>().FindAsync(
            item => item.ItemType == ModerationItemType.RoadmapTemplateReview
                    && item.ReadingRoadmapId == rid
                    && (item.Status == ModerationItemStatus.Pending
                        || item.Status == ModerationItemStatus.Claimed),
            cancellationToken: cancellationToken);
        var openItem = openItems.OrderByDescending(item => item.Id).FirstOrDefault();
        if (openItem == null)
        {
            throw new ConflictException("Không tìm thấy yêu cầu duyệt đang mở của bộ này.");
        }

        var item = await _unitOfWork.Repository<ModerationItem>().GetByIdAsync(openItem.Id, cancellationToken)
                   ?? throw new NotFoundException("Mục kiểm duyệt", openItem.Id);
        if (item.Status != ModerationItemStatus.Pending)
        {
            throw new ConflictException("Mục đã có người nhận hoặc đã xử lý xong nên không thể rút.");
        }

        item.Status = ModerationItemStatus.Withdrawn;
        item.WithdrawnAt = DateTime.UtcNow;
        item.RowVersion += 1;
        _unitOfWork.Repository<ModerationItem>().Update(item);
        roadmap.Status = RoadmapTemplateStatus.Draft;
        roadmap.SubmittedAt = null;
        _unitOfWork.Repository<ReadingRoadmap>().Update(roadmap);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            actor.Id, "RoadmapTemplateWithdrawn", nameof(ReadingRoadmap), roadmap.Id,
            beforeState: new { status = RoadmapTemplateStatus.PendingReview.ToString() },
            afterState: new
            {
                status = RoadmapTemplateStatus.Draft.ToString(),
                moderationItemId = item.Id
            },
            cancellationToken: cancellationToken);

        return new RoadmapSubmitResultDto
        {
            RoadmapId = roadmap.Id,
            Status = roadmap.Status.ToString(),
            ItemId = item.Id
        };
    }
}
