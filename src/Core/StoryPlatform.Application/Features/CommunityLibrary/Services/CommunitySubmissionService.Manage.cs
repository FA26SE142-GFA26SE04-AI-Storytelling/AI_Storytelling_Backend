using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.CommunityLibrary.Services;

// Rút, gỡ và danh sách các lần nộp (WF-L4F UC-L4F-03).
public partial class CommunitySubmissionService
{
    private const int ListLimit = 50;

    public async Task<CommunitySubmissionDto> WithdrawAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default)
    {
        var actor = await RequireParentAsync(actorUserId, cancellationToken);
        var item = await LoadOwnItemAsync(actor, itemId, cancellationToken);
        if (item.Status != ModerationItemStatus.Pending)
        {
            throw new ConflictException("Mục đã có người nhận hoặc đã xử lý xong nên không thể rút.");
        }

        item.Status = ModerationItemStatus.Withdrawn;
        item.WithdrawnAt = DateTime.UtcNow;
        item.RowVersion += 1;
        _unitOfWork.Repository<ModerationItem>().Update(item);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            actor.Id, "CommunitySubmissionWithdrawn", nameof(ModerationItem), item.Id,
            beforeState: new { status = ModerationItemStatus.Pending.ToString() },
            afterState: new { status = ModerationItemStatus.Withdrawn.ToString(), storyId = item.StoryId },
            cancellationToken: cancellationToken);

        return await ToDtoAsync(item, cancellationToken);
    }

    public async Task<CommunitySubmissionDto> RemoveAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default)
    {
        var actor = await RequireParentAsync(actorUserId, cancellationToken);
        var item = await LoadOwnItemAsync(actor, itemId, cancellationToken);
        if (item.Status != ModerationItemStatus.Approved)
        {
            throw new ConflictException("Chỉ gỡ được truyện đã được duyệt và đang có trong thư viện cộng đồng.");
        }

        item.Status = ModerationItemStatus.Removed;
        item.RemovedAt = DateTime.UtcNow;
        item.ClosedReason = "submitter_removed";
        item.RowVersion += 1;
        _unitOfWork.Repository<ModerationItem>().Update(item);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            actor.Id, "CommunitySubmissionRemoved", nameof(ModerationItem), item.Id,
            beforeState: new { status = ModerationItemStatus.Approved.ToString() },
            afterState: new { status = ModerationItemStatus.Removed.ToString(), storyId = item.StoryId },
            cancellationToken: cancellationToken);

        return await ToDtoAsync(item, cancellationToken);
    }

    public async Task<List<CommunitySubmissionDto>> ListMineAsync(
        int actorUserId, CancellationToken cancellationToken = default)
    {
        var actor = await RequireParentAsync(actorUserId, cancellationToken);
        var actorId = actor.Id;
        var items = (await _unitOfWork.Repository<ModerationItem>().FindAsync(
                item => item.ItemType == ModerationItemType.CommunitySubmission
                        && item.CreatedByUserId == actorId
                        && !item.IsDeleted,
                cancellationToken: cancellationToken))
            .OrderByDescending(item => item.Id).Take(ListLimit).ToList();

        var storyIds = items.Where(item => item.StoryId.HasValue).Select(item => item.StoryId!.Value).Distinct().ToList();
        var stories = await _unitOfWork.Repository<Story>().FindAsync(
            story => storyIds.Contains(story.Id), cancellationToken: cancellationToken);
        var titles = stories.ToDictionary(story => story.Id, story => story.Title);

        return items.Select(item => Map(item, item.StoryId.HasValue ? titles.GetValueOrDefault(item.StoryId.Value) : null))
            .ToList();
    }

    private async Task<ModerationItem> LoadOwnItemAsync(
        UserAccount actor, int itemId, CancellationToken cancellationToken)
    {
        var item = await _unitOfWork.Repository<ModerationItem>().GetByIdAsync(itemId, cancellationToken);
        if (item == null || item.IsDeleted)
        {
            throw new NotFoundException("Lần nộp Community", itemId);
        }

        if (item.ItemType != ModerationItemType.CommunitySubmission)
        {
            throw new BadRequestException("Mục này không phải lần nộp lên thư viện cộng đồng.");
        }

        if (item.CreatedByUserId != actor.Id)
        {
            throw new ForbiddenException("Bạn chỉ thao tác được trên lần nộp do chính mình gửi.");
        }

        return item;
    }

    private async Task<CommunitySubmissionDto> ToDtoAsync(ModerationItem item, CancellationToken cancellationToken)
    {
        string? title = null;
        if (item.StoryId.HasValue)
        {
            var story = await _unitOfWork.Repository<Story>().GetByIdAsync(item.StoryId.Value, cancellationToken);
            title = story?.Title;
        }

        return Map(item, title);
    }

    /// <summary>Trạng thái dễ hiểu cho phụ huynh: Claimed vẫn là "chờ"; Closed do hệ thống tính là "đã rút".</summary>
    private static CommunitySubmissionDto Map(ModerationItem item, string? storyTitle)
    {
        var status = item.Status switch
        {
            ModerationItemStatus.Pending or ModerationItemStatus.Claimed => "Pending",
            ModerationItemStatus.Approved => "Approved",
            ModerationItemStatus.Rejected => "Rejected",
            ModerationItemStatus.Removed => "Removed",
            _ => "Withdrawn"
        };

        return new CommunitySubmissionDto
        {
            ItemId = item.Id,
            StoryId = item.StoryId ?? 0,
            StoryTitle = storyTitle,
            Status = status,
            SubmittedAt = item.CreatedAt,
            DecidedAt = item.DecidedAt,
            RejectReason = item.Status == ModerationItemStatus.Rejected ? item.DecisionReason : null,
            CanWithdraw = item.Status == ModerationItemStatus.Pending,
            CanRemove = item.Status == ModerationItemStatus.Approved
        };
    }
}
