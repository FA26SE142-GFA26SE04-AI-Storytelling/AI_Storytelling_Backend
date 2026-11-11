using StoryPlatform.Application.Abstractions.Persistence;
using System.Linq.Expressions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation.Services;

public class ModerationQueueService : IModerationQueueService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IStoryUsageImpactReader? _storyImpactReader;

    public ModerationQueueService(
        IUnitOfWork unitOfWork, IAuditLogWriter auditLogWriter, IStoryUsageImpactReader? storyImpactReader = null)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _storyImpactReader = storyImpactReader;
    }

    public async Task<ModerationItem> EnqueueAsync(
        EnqueueModerationItemCommand command, CancellationToken cancellationToken = default)
    {
        var item = new ModerationItem
        {
            ItemType = command.ItemType,
            Status = ModerationItemStatus.Pending,
            Priority = ModerationQueuePolicy.GetPriority(command.ItemType, command.IsSafetyConcern),
            StoryId = command.StoryId,
            StoryVersionId = command.StoryVersionId,
            ContentReportId = command.ContentReportId,
            ReadingRoadmapId = command.ReadingRoadmapId,
            CreatedByUserId = command.CreatedByUserId,
            SourceChildProfileId = command.SourceChildProfileId,
            IsResubmission = command.IsResubmission,
            PreviousItemId = command.PreviousItemId,
            SlaDueAt = DateTime.UtcNow.Add(ModerationQueuePolicy.GetSla(command.ItemType))
        };

        await _unitOfWork.Repository<ModerationItem>().AddAsync(item, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task<PagedResult<ModerationItemSummaryDto>> ListAsync(
        int actorUserId, ModerationQueueQueryDto query, CancellationToken cancellationToken = default)
    {
        var actor = await RequireStaffAsync(actorUserId, cancellationToken);
        var status = query.Status ?? ModerationItemStatus.Pending;
        var itemType = query.ItemType;
        var priority = query.Priority;
        var onlyOverdue = query.OnlyOverdue;
        var hideOwnItems = actor.Role == UserRole.Moderator;
        var actorId = actor.Id;
        var now = DateTime.UtcNow;
        var pageIndex = Math.Max(query.PageIndex, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        Expression<Func<ModerationItem, bool>> filter = item =>
            item.Status == status
            && (itemType == null || item.ItemType == itemType)
            && (priority == null || item.Priority == priority)
            && (!onlyOverdue || (item.SlaDueAt != null && item.SlaDueAt < now))
            && (!hideOwnItems || item.CreatedByUserId != actorId);

        var resubmissionCutoff = now - ModerationQueuePolicy.ResubmissionPriorityWindow;
        // Priority lưu dạng chuỗi nên phải sắp theo biểu thức CASE, không sắp trực tiếp theo cột.
        var (items, totalCount) = await _unitOfWork.Repository<ModerationItem>().GetPagedAsync(
            pageIndex,
            pageSize,
            filter,
            orderBy: source => source
                .OrderBy(item => item.IsResubmission
                                 && item.PreviousItem != null
                                 && item.PreviousItem.DecidedByUserId == actorId
                                 && item.CreatedAt > resubmissionCutoff ? 0 : 1)
                .ThenBy(item => item.Priority == ModerationPriority.High ? 0
                    : item.Priority == ModerationPriority.Medium ? 1 : 2)
                .ThenBy(item => item.SlaDueAt)
                .ThenBy(item => item.Id),
            includeProperties: $"{nameof(ModerationItem.Story)},{nameof(ModerationItem.StoryVersion)},{nameof(ModerationItem.CreatedByUser)},{nameof(ModerationItem.ClaimedByUser)}",
            cancellationToken: cancellationToken);

        return new PagedResult<ModerationItemSummaryDto>(
            items.Select(item => MapSummary(item, now)).ToList(), totalCount, pageIndex, pageSize);
    }

    public async Task<ModerationItemDetailDto> ClaimAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default)
    {
        var actor = await RequireStaffAsync(actorUserId, cancellationToken);
        var repository = _unitOfWork.Repository<ModerationItem>();
        var item = await repository.GetByIdAsync(itemId, cancellationToken)
                   ?? throw new NotFoundException("Mục kiểm duyệt", itemId);

        if (item.Status != ModerationItemStatus.Pending)
        {
            throw new ConflictException("Mục này đã có người nhận hoặc đã xử lý xong.");
        }

        if (item.CreatedByUserId == actor.Id)
        {
            throw new ForbiddenException("Bạn không thể duyệt nội dung do chính mình tạo hoặc nộp.");
        }

        if (actor.Role == UserRole.Administrator)
        {
            await EnsureAdministratorMayClaimAsync(item, cancellationToken);
        }

        var now = DateTime.UtcNow;
        item.Status = ModerationItemStatus.Claimed;
        item.ClaimedByUserId = actor.Id;
        item.ClaimedAt = now;
        // RowVersion là token đồng thời: nếu người khác vừa nhận thì SaveChanges ném ConflictException (409).
        item.RowVersion += 1;
        repository.Update(item);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            actor.Id, "ModerationItemClaimed", nameof(ModerationItem), item.Id,
            beforeState: new { status = ModerationItemStatus.Pending.ToString() },
            afterState: new { status = ModerationItemStatus.Claimed.ToString(), claimedBy = actor.Id },
            cancellationToken: cancellationToken);

        await HydrateSummaryRelationsAsync(item, cancellationToken);
        var claimed = MapDetail(item, now);
        claimed.ClaimedByName = DisplayName(actor);
        claimed.StoryImpact = await ReadStoryImpactAsync(item, cancellationToken);
        return claimed;
    }

    public async Task<ModerationItemDetailDto> OpenAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default)
    {
        var actor = await RequireStaffAsync(actorUserId, cancellationToken);
        var item = await _unitOfWork.Repository<ModerationItem>().GetByIdAsync(itemId, cancellationToken)
                   ?? throw new NotFoundException("Mục kiểm duyệt", itemId);

        if (item.Status != ModerationItemStatus.Claimed || item.ClaimedByUserId != actor.Id)
        {
            throw new ForbiddenException("Bạn chỉ được đọc mục đang do chính mình nhận.");
        }

        var now = DateTime.UtcNow;
        if (item.ClaimedAt == null || item.ClaimedAt.Value.Add(ModerationQueuePolicy.ClaimTimeout) <= now)
        {
            // Tác vụ quét nền có thể chưa kịp trả mục về hàng đợi; vẫn không cho đọc quá 2 giờ.
            throw new ConflictException("Mục đã quá 2 giờ kể từ lúc nhận và sẽ được trả về hàng đợi. Vui lòng nhận lại.");
        }

        await _auditLogWriter.LogAsync(
            actor.Id, "ModerationItemOpened", nameof(ModerationItem), item.Id,
            beforeState: null, afterState: new { itemType = item.ItemType.ToString() },
            cancellationToken: cancellationToken);

        await HydrateSummaryRelationsAsync(item, cancellationToken);
        var opened = MapDetail(item, now);
        opened.ClaimedByName = DisplayName(actor);
        opened.StoryImpact = await ReadStoryImpactAsync(item, cancellationToken);
        return opened;
    }

    private async Task<UserAccount> RequireStaffAsync(int actorUserId, CancellationToken cancellationToken)
    {
        var actor = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(actorUserId, cancellationToken);
        if (actor == null
            || actor.Status != AccountStatus.Active
            || actor.Role is not (UserRole.Moderator or UserRole.Administrator))
        {
            throw new ForbiddenException(
                "Chỉ Moderator hoặc Administrator đang hoạt động mới dùng được hàng đợi kiểm duyệt.");
        }

        return actor;
    }

    /// <summary>
    /// Administrator chỉ nhận mục khi không còn Moderator active nào khác người tạo mục
    /// (không còn Moderator, hoặc Moderator duy nhất chính là người tạo).
    /// </summary>
    private async Task EnsureAdministratorMayClaimAsync(ModerationItem item, CancellationToken cancellationToken)
    {
        var creatorId = item.CreatedByUserId ?? 0;
        var eligibleModerators = await _unitOfWork.Repository<UserAccount>().CountAsync(
            user => user.Role == UserRole.Moderator && user.Status == AccountStatus.Active && user.Id != creatorId,
            cancellationToken);
        if (eligibleModerators > 0)
        {
            throw new ForbiddenException(
                "Administrator chỉ xử lý hàng đợi khi không còn Moderator hợp lệ để nhận mục này.");
        }
    }

    private static string? DisplayName(UserAccount? user) => ModerationQueuePolicy.DisplayName(user);

    private async Task HydrateSummaryRelationsAsync(ModerationItem item, CancellationToken cancellationToken)
    {
        var includeCreatedBy = item.ItemType == ModerationItemType.PublicStoryReview && item.CreatedByUserId.HasValue;
        if (!item.StoryId.HasValue && !item.StoryVersionId.HasValue && !includeCreatedBy)
            return;

        // These navigations belong to a tracked queue item. Load tracked entities as well:
        // attaching an AsNoTracking version makes its separately stored owned metadata Added,
        // causing duplicate-key INSERTs when a later decision/audit saves this unit of work.
        if (item.Story == null && item.StoryId.HasValue)
        {
            var storyRepo = _unitOfWork.Repository<Story>();
            if (storyRepo != null)
                item.Story = await storyRepo.GetByIdAsync(item.StoryId.Value, cancellationToken);
        }
        if (item.StoryVersion == null && item.StoryVersionId.HasValue)
        {
            var versionRepo = _unitOfWork.Repository<StoryVersion>();
            if (versionRepo != null)
                item.StoryVersion = await versionRepo.GetByIdAsync(item.StoryVersionId.Value, cancellationToken);
        }
        if (includeCreatedBy && item.CreatedByUser == null)
        {
            var userRepo = _unitOfWork.Repository<UserAccount>();
            if (userRepo != null)
                item.CreatedByUser = await userRepo.GetByIdAsync(item.CreatedByUserId!.Value, cancellationToken);
        }
    }

    /// <summary>Chỉ mục gỡ / duyệt công khai truyện Creator mới có thông tin ảnh hưởng.</summary>
    private async Task<StoryImpactDto?> ReadStoryImpactAsync(ModerationItem item, CancellationToken cancellationToken)
    {
        if (_storyImpactReader == null || !item.StoryId.HasValue
            || item.ItemType is not (ModerationItemType.PublicStoryRemoval or ModerationItemType.PublicStoryReview))
        {
            return null;
        }

        return await _storyImpactReader.ReadAsync(
            item.StoryId.Value, item.ItemType == ModerationItemType.PublicStoryReview, cancellationToken);
    }

    private static ModerationItemSummaryDto MapSummary(ModerationItem item, DateTime now) =>
        FillSummary(new ModerationItemSummaryDto(), item, now);

    private static ModerationItemDetailDto MapDetail(ModerationItem item, DateTime now)
    {
        var detail = FillSummary(new ModerationItemDetailDto(), item, now);
        detail.RowVersion = item.RowVersion;
        detail.Flags = item.Flags;
        detail.Checklist = item.Checklist;
        detail.ReviewProgress = item.ReviewProgress;
        return detail;
    }

    private static T FillSummary<T>(T dto, ModerationItem item, DateTime now) where T : ModerationItemSummaryDto
    {
        dto.Id = item.Id;
        dto.ItemType = item.ItemType.ToString();
        dto.Status = item.Status.ToString();
        dto.Priority = item.Priority.ToString();
        var isPublicStoryReview = item.ItemType == ModerationItemType.PublicStoryReview;
        dto.StoryId = item.StoryId;
        dto.StoryTitle = !string.IsNullOrWhiteSpace(item.StoryVersion?.Title)
            ? item.StoryVersion.Title
            : item.Story?.Title;
        dto.StoryVersionId = item.StoryVersionId;
        dto.ContentReportId = item.ContentReportId;
        dto.ReadingRoadmapId = item.ReadingRoadmapId;
        dto.CreatedByUserId = isPublicStoryReview ? item.CreatedByUserId : null;
        dto.CreatedByName = isPublicStoryReview ? DisplayName(item.CreatedByUser) : null;
        dto.IsResubmission = item.IsResubmission;
        dto.SlaDueAt = item.SlaDueAt;
        dto.IsOverdue = item.SlaDueAt != null && item.SlaDueAt < now
                        && item.Status is ModerationItemStatus.Pending or ModerationItemStatus.Claimed;
        dto.ClaimedByUserId = item.ClaimedByUserId;
        dto.ClaimedByName = DisplayName(item.ClaimedByUser);
        dto.ClaimedAt = item.ClaimedAt;
        dto.ClaimExpiresAt = item.Status == ModerationItemStatus.Claimed && item.ClaimedAt != null
            ? item.ClaimedAt.Value.Add(ModerationQueuePolicy.ClaimTimeout)
            : null;
        dto.CreatedAt = item.CreatedAt;
        return dto;
    }
}
