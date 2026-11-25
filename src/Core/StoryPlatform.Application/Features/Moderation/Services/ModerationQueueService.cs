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
    private const string QueueIncludes =
        $"{nameof(ModerationItem.Story)},{nameof(ModerationItem.StoryVersion)},{nameof(ModerationItem.CreatedByUser)},{nameof(ModerationItem.ClaimedByUser)},{nameof(ModerationItem.ContentReport)}";

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
            Priority = ModerationQueuePolicy.GetPriority(command.ItemType, command.IsSafetyConcern, command.IsPartnerCreator),
            StoryId = command.StoryId,
            StoryVersionId = command.StoryVersionId,
            ContentReportId = command.ContentReportId,
            StoryRatingId = command.StoryRatingId,
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
        var onlyMyEventSubmissions = query.OnlyMyEventSubmissions;
        var hideOwnItems = actor.Role == UserRole.Moderator;
        var actorId = actor.Id;
        var now = DateTime.UtcNow;
        var pageIndex = Math.Max(query.PageIndex, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        if (onlyMyEventSubmissions && itemType != null && itemType != ModerationItemType.PublicStoryReview)
        {
            return new PagedResult<ModerationItemSummaryDto>(Array.Empty<ModerationItemSummaryDto>(), 0, pageIndex, pageSize);
        }

        Expression<Func<ModerationItem, bool>> filter = item =>
            item.Status == status
            && (itemType == null || item.ItemType == itemType)
            && (priority == null || item.Priority == priority)
            && (!onlyOverdue || (item.SlaDueAt != null && item.SlaDueAt < now))
            && (!hideOwnItems || item.CreatedByUserId != actorId);

        var resubmissionCutoff = now - ModerationQueuePolicy.ResubmissionPriorityWindow;
        var hasEventRepos = _unitOfWork.Repository<EventSubmission>() != null && _unitOfWork.Repository<Event>() != null;

        if (onlyMyEventSubmissions && !hasEventRepos)
        {
            return new PagedResult<ModerationItemSummaryDto>(Array.Empty<ModerationItemSummaryDto>(), 0, pageIndex, pageSize);
        }

        Dictionary<(int StoryId, int StoryVersionId), ActiveVersionEventInfo>? activeMap = null;
        Expression<Func<ModerationItem, int>>? activeOrder = null;
        var activeQueue = status is ModerationItemStatus.Pending or ModerationItemStatus.Claimed;
        if (hasEventRepos && (itemType == null || itemType == ModerationItemType.PublicStoryReview))
        {
            if (activeQueue)
            {
                activeMap = await EventSubmissionModerationAccess.LoadActiveByVersionAsync(_unitOfWork, now, cancellationToken);
                if (activeMap.Count > 0 || onlyMyEventSubmissions)
                {
                    var visible = activeMap.Where(pair => !hideOwnItems || pair.Value.CanModeratorReview(actorId)).ToArray();
                    var hiddenVersionIds = activeMap.Keys.Except(visible.Select(pair => pair.Key))
                        .Select(key => key.StoryVersionId).ToList();
                    var visibleVersionIds = visible.Select(pair => pair.Key.StoryVersionId).ToList();
                    filter = And(filter, item => onlyMyEventSubmissions
                        ? item.ItemType == ModerationItemType.PublicStoryReview && item.StoryId.HasValue && item.StoryVersionId.HasValue
                            && visibleVersionIds.Contains(item.StoryVersionId.Value)
                        : item.ItemType != ModerationItemType.PublicStoryReview || !item.StoryId.HasValue || !item.StoryVersionId.HasValue
                            || !hiddenVersionIds.Contains(item.StoryVersionId.Value));
                    activeOrder = ActiveEventPriority(visible);
                }
            }
            else if (onlyMyEventSubmissions)
            {
                var eventIds = _unitOfWork.Repository<Event>().Query().Where(e => !e.IsDeleted).Select(e => e.Id);
                var submissions = _unitOfWork.Repository<EventSubmission>().Query()
                    .Where(s => !s.IsDeleted && s.ParentSubmissionText == null && eventIds.Contains(s.EventId));
                var assignedIds = hideOwnItems
                    ? (await EventSubmissionModerationAccess.LoadAssignedEventIdsAsync(_unitOfWork, actorId, cancellationToken)).ToList()
                    : [];
                filter = And(filter, item => item.ItemType == ModerationItemType.PublicStoryReview
                    && item.StoryId.HasValue && item.StoryVersionId.HasValue
                    && submissions.Any(s => s.StoryId == item.StoryId
                        && (s.CurrentStoryVersionId == item.StoryVersionId || s.LockedStoryVersionId == item.StoryVersionId))
                    && (!hideOwnItems || !submissions.Any(s => s.StoryId == item.StoryId
                        && (s.CurrentStoryVersionId == item.StoryVersionId || s.LockedStoryVersionId == item.StoryVersionId)
                        && !assignedIds.Contains(s.EventId))));
            }
        }
        var (items, totalCount) = await _unitOfWork.Repository<ModerationItem>().GetPagedAsync(
            pageIndex,
            pageSize,
            filter,
            orderBy: source => (activeOrder == null ? source.OrderBy(item => 0) : source.OrderBy(activeOrder))
                .ThenBy(item => item.IsResubmission
                                 && item.PreviousItem != null
                                 && item.PreviousItem.DecidedByUserId == actorId
                                 && item.CreatedAt > resubmissionCutoff ? 0 : 1)
                .ThenBy(item => item.Priority == ModerationPriority.High ? 0
                    : item.Priority == ModerationPriority.Medium ? 1 : 2)
                .ThenBy(item => activeOrder != null || onlyMyEventSubmissions ? item.SlaDueAt ?? DateTime.MaxValue : item.SlaDueAt)
                .ThenBy(item => item.Id),
            includeProperties: QueueIncludes,
            cancellationToken: cancellationToken);

        var summaries = items.Select(item => MapSummary(item, now)).ToList();
        if (activeMap != null)
        {
            foreach (var summary in summaries)
            {
                if (summary.ItemType == nameof(ModerationItemType.PublicStoryReview)
                    && summary.StoryId is int storyId && summary.StoryVersionId is int versionId
                    && activeMap.TryGetValue((storyId, versionId), out var info))
                {
                    summary.EventSubmissions = info.Events;
                }
            }
        }
        if (hasEventRepos
            && summaries.Count > 0
            && status is not (ModerationItemStatus.Pending or ModerationItemStatus.Claimed)
            && (itemType == null || itemType == ModerationItemType.PublicStoryReview))
        {
            var historicalMap = await EventSubmissionModerationAccess.LoadHistoricalMapForItemsAsync(
                _unitOfWork, items, cancellationToken);
            foreach (var summary in summaries)
            {
                if (summary.StoryId.HasValue
                    && summary.StoryVersionId.HasValue
                    && historicalMap.TryGetValue((summary.StoryId.Value, summary.StoryVersionId.Value), out var linked))
                {
                    summary.EventSubmissions = linked;
                }
            }
        }

        return new PagedResult<ModerationItemSummaryDto>(summaries, totalCount, pageIndex, pageSize);
    }

    private static Expression<Func<ModerationItem, bool>> And(
        Expression<Func<ModerationItem, bool>> first, Expression<Func<ModerationItem, bool>> second)
    {
        var combined = Expression.AndAlso(first.Body, Expression.Invoke(second, first.Parameters[0]));
        return Expression.Lambda<Func<ModerationItem, bool>>(combined, first.Parameters);
    }

    private static Expression<Func<ModerationItem, int>> ActiveEventPriority(
        IEnumerable<KeyValuePair<(int StoryId, int StoryVersionId), ActiveVersionEventInfo>> visible)
    {
        var deadlineGroups = visible.GroupBy(pair => pair.Value.NearestReviewCutoffAt ?? DateTime.MaxValue)
            .OrderBy(group => group.Key).ToArray();
        var item = Expression.Parameter(typeof(ModerationItem), "item");
        Expression rank = Expression.Constant(deadlineGroups.Length);
        for (var i = deadlineGroups.Length - 1; i >= 0; i--)
        {
            var versionIds = deadlineGroups[i].Select(pair => pair.Key.StoryVersionId).ToList();
            Expression<Func<ModerationItem, bool>> matches = entry =>
                entry.ItemType == ModerationItemType.PublicStoryReview && entry.StoryId.HasValue && entry.StoryVersionId.HasValue
                && versionIds.Contains(entry.StoryVersionId.Value);
            rank = Expression.Condition(Expression.Invoke(matches, item), Expression.Constant(i), rank);
        }

        return Expression.Lambda<Func<ModerationItem, int>>(rank, item);
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
            // Report nội dung và đánh giá truyện chỉ do Moderator xử lý, kể cả khi không còn Moderator.
            if (item.ItemType is ModerationItemType.ContentReport or ModerationItemType.StoryRating)
            {
                throw new ForbiddenException("Report nội dung và đánh giá truyện chỉ do kiểm duyệt viên xử lý.");
            }

            await EnsureAdministratorMayClaimAsync(item, cancellationToken);
        }

        var now = DateTime.UtcNow;
        var hasEventRepos = item.ItemType == ModerationItemType.PublicStoryReview
                            && item.StoryId.HasValue
                            && item.StoryVersionId.HasValue
                            && _unitOfWork.Repository<EventSubmission>() != null
                            && _unitOfWork.Repository<Event>() != null;

        if (hasEventRepos)
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                await EventSubmissionModerationAccess.AcquireEventAndStoryLocksAsync(
                    _unitOfWork, item.StoryId!.Value, item.StoryVersionId!.Value, now, cancellationToken);

                var fresh = await repository.FirstOrDefaultAsync(x => x.Id == itemId, cancellationToken: cancellationToken)
                            ?? throw new NotFoundException("Mục kiểm duyệt", itemId);
                if (fresh.Status != ModerationItemStatus.Pending)
                {
                    throw new ConflictException("Mục này đã có người nhận hoặc đã xử lý xong.");
                }

                if (fresh.RowVersion != item.RowVersion)
                {
                    _unitOfWork.DiscardTrackedChanges();
                    item = await repository.GetByIdAsync(itemId, cancellationToken)
                           ?? throw new NotFoundException("Mục kiểm duyệt", itemId);
                    actor = await RequireStaffAsync(actorUserId, cancellationToken);
                }

                var access = await EventSubmissionModerationAccess.CheckActorAccessAsync(
                    _unitOfWork, actor, item, now, cancellationToken);
                if (!access.IsAllowed)
                {
                    throw new ForbiddenException(access.DenialReason!);
                }

                item.Status = ModerationItemStatus.Claimed;
                item.ClaimedByUserId = actor.Id;
                item.ClaimedAt = now;
                item.RowVersion += 1;
                repository.Update(item);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _auditLogWriter.LogAsync(
                    actor.Id, "ModerationItemClaimed", nameof(ModerationItem), item.Id,
                    beforeState: new { status = ModerationItemStatus.Pending.ToString() },
                    afterState: new { status = ModerationItemStatus.Claimed.ToString(), claimedBy = actor.Id },
                    cancellationToken: cancellationToken);

                await _unitOfWork.CommitTransactionAsync(cancellationToken);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                _unitOfWork.DiscardTrackedChanges();
                throw;
            }
        }
        else
        {
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
        }

        await HydrateSummaryRelationsAsync(item, cancellationToken);
        var claimed = MapDetail(item, now);
        claimed.ClaimedByName = DisplayName(actor);
        claimed.StoryImpact = await ReadStoryImpactAsync(item, cancellationToken);
        claimed.EventSubmissions = await EventSubmissionModerationAccess.LoadLinkedForItemAsync(
            _unitOfWork, item, now, cancellationToken);
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

        if (item.ItemType == ModerationItemType.PublicStoryReview
            && _unitOfWork.Repository<EventSubmission>() != null
            && _unitOfWork.Repository<Event>() != null)
        {
            var access = await EventSubmissionModerationAccess.CheckActorAccessAsync(
                _unitOfWork, actor, item, now, cancellationToken);
            if (!access.IsAllowed)
            {
                throw new ForbiddenException(access.DenialReason!);
            }
        }

        await _auditLogWriter.LogAsync(
            actor.Id, "ModerationItemOpened", nameof(ModerationItem), item.Id,
            beforeState: null, afterState: new { itemType = item.ItemType.ToString() },
            cancellationToken: cancellationToken);

        await HydrateSummaryRelationsAsync(item, cancellationToken);
        var opened = MapDetail(item, now);
        opened.ClaimedByName = DisplayName(actor);
        opened.StoryImpact = await ReadStoryImpactAsync(item, cancellationToken);
        opened.EventSubmissions = await EventSubmissionModerationAccess.LoadLinkedForItemAsync(
            _unitOfWork, item, now, cancellationToken);
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
                "Chỉ kiểm duyệt viên hoặc quản trị viên đang hoạt động mới dùng được hàng đợi kiểm duyệt.");
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
                "Quản trị viên chỉ xử lý hàng đợi khi không còn kiểm duyệt viên hợp lệ để nhận mục này.");
        }
    }

    private static string? DisplayName(UserAccount? user) => ModerationQueuePolicy.DisplayName(user);

    /// <summary>
    /// Chỉ trả người gửi khi đó là Creator (gửi duyệt / xin gỡ truyện công khai) hoặc Moderator
    /// (yêu cầu rà soát nội dung đang công khai). Report của phụ huynh luôn ẩn danh.
    /// </summary>
    private static bool ExposesCreatedBy(ModerationItem item) =>
        item.ItemType is ModerationItemType.PublicStoryReview or ModerationItemType.PublicStoryRemoval
        || (item.ItemType == ModerationItemType.ContentReport
            && item.ContentReport is { IsDeleted: false, IsReviewItem: true });

    private async Task HydrateSummaryRelationsAsync(ModerationItem item, CancellationToken cancellationToken)
    {
        // Cần đọc report để phân biệt yêu cầu rà soát của Moderator với report của phụ huynh.
        if (item.ItemType == ModerationItemType.ContentReport
            && item.ContentReport == null
            && item.ContentReportId.HasValue)
        {
            var reportRepo = _unitOfWork.Repository<ContentReport>();
            if (reportRepo != null)
                item.ContentReport = await reportRepo.GetByIdAsync(item.ContentReportId.Value, cancellationToken);
        }

        var includeCreatedBy = ExposesCreatedBy(item) && item.CreatedByUserId.HasValue;
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
        var exposesCreatedBy = ExposesCreatedBy(item);
        dto.StoryId = item.StoryId;
        dto.StoryTitle = !string.IsNullOrWhiteSpace(item.StoryVersion?.Title)
            ? item.StoryVersion.Title
            : item.Story?.Title;
        dto.StoryVersionId = item.StoryVersionId;
        dto.ContentReportId = item.ContentReportId;
        dto.ReadingRoadmapId = item.ReadingRoadmapId;
        dto.CreatedByUserId = exposesCreatedBy ? item.CreatedByUserId : null;
        dto.CreatedByName = exposesCreatedBy ? DisplayName(item.CreatedByUser) : null;
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
