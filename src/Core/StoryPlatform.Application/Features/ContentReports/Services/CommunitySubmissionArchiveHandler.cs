using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentReports.Services;

/// <summary>
/// 3D: truyện bị Archive vì an toàn / bản quyền / không phù hợp thì submission Community của truyện đó bị gỡ
/// (WF-L4F UC-L4F-03, S4 Mục 4): đã duyệt → Removed, đang chờ → Withdrawn, đang có Moderator giữ → Closed (báo
/// Moderator đó). Người nộp nhận một thông báo chỉ nêu lý do chung. supervisor_preference giữ nguyên bản trong thư viện.
/// </summary>
public class CommunitySubmissionArchiveHandler : IStoryArchiveHandler
{
    public const string ClosedReason = "story_archived";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INotificationService _notificationService;

    public CommunitySubmissionArchiveHandler(
        IUnitOfWork unitOfWork, IAuditLogWriter auditLogWriter, INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _notificationService = notificationService;
    }

    public async Task HandleAsync(StoryArchiveContext context, CancellationToken cancellationToken = default)
    {
        if (context.Reason == ArchivedReason.SupervisorPreference)
        {
            return;
        }

        var storyId = context.Story.Id;
        var itemRepository = _unitOfWork.Repository<ModerationItem>();
        var open = await itemRepository.FindAsync(
            item => item.ItemType == ModerationItemType.CommunitySubmission
                    && item.StoryId == storyId
                    && (item.Status == ModerationItemStatus.Pending
                        || item.Status == ModerationItemStatus.Claimed
                        || item.Status == ModerationItemStatus.Approved),
            cancellationToken: cancellationToken);
        if (open.Count == 0)
        {
            return;
        }

        var changed = new List<(ModerationItem Item, ModerationItemStatus Before, int? ClaimedBy)>();
        foreach (var candidate in open)
        {
            // FindAsync trả bản không theo dõi: lấy bản đang theo dõi trước khi tăng RowVersion.
            var item = await itemRepository.GetByIdAsync(candidate.Id, cancellationToken);
            if (item == null || item.IsDeleted
                || item.Status is not (ModerationItemStatus.Pending or ModerationItemStatus.Claimed
                    or ModerationItemStatus.Approved))
            {
                continue;
            }

            var before = item.Status;
            var claimedBy = item.ClaimedByUserId;
            switch (before)
            {
                case ModerationItemStatus.Approved:
                    item.Status = ModerationItemStatus.Removed;
                    item.RemovedAt = context.Now;
                    break;
                case ModerationItemStatus.Pending:
                    item.Status = ModerationItemStatus.Withdrawn;
                    item.WithdrawnAt = context.Now;
                    break;
                default:
                    item.Status = ModerationItemStatus.Closed;
                    item.DecidedAt = context.Now;
                    break;
            }

            item.ClosedReason = ClosedReason;
            item.RowVersion += 1;
            itemRepository.Update(item);
            changed.Add((item, before, claimedBy));
        }

        if (changed.Count == 0)
        {
            return;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var notifiedSubmitters = new HashSet<int>();
        foreach (var (item, before, claimedBy) in changed)
        {
            await _auditLogWriter.LogAsync(
                context.ActorUserId, "CommunitySubmissionClosedByArchive", nameof(ModerationItem), item.Id,
                beforeState: new { status = before.ToString() },
                afterState: new
                {
                    status = item.Status.ToString(),
                    storyId,
                    archivedReason = context.Reason.ToString()
                },
                cancellationToken: cancellationToken);

            if (before == ModerationItemStatus.Claimed && claimedBy.HasValue)
            {
                await NotifyClaimantAsync(item, storyId, claimedBy.Value, cancellationToken);
            }

            if (item.CreatedByUserId.HasValue && notifiedSubmitters.Add(item.CreatedByUserId.Value))
            {
                await NotifySubmitterAsync(context, item.CreatedByUserId.Value, cancellationToken);
            }
        }
    }

    private async Task NotifyClaimantAsync(
        ModerationItem item, int storyId, int claimantUserId, CancellationToken cancellationToken)
    {
        var claimant = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(claimantUserId, cancellationToken);
        if (claimant == null || claimant.IsDeleted || claimant.Status != AccountStatus.Active)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            itemId = item.Id,
            itemType = ModerationItemType.CommunitySubmission.ToString(),
            storyId,
            decision = "closed",
            reason = ClosedReason
        });
        await _notificationService.CreateAsync(
            claimant.Id, NotificationType.ContentModerationResult, payload, cancellationToken);
    }

    /// <summary>
    /// Một thông báo cho người nộp, chỉ nêu lý do chung. Bỏ qua khi người nộp chính là tác giả (đã nhận thông báo ở
    /// quyết định Archive) hoặc chính người báo cáo (đã nhận kết quả report), hoặc không còn hoạt động.
    /// </summary>
    private async Task NotifySubmitterAsync(
        StoryArchiveContext context, int submitterUserId, CancellationToken cancellationToken)
    {
        if (submitterUserId == context.Story.AuthorUserId || submitterUserId == context.ReporterUserId)
        {
            return;
        }

        var submitter = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(submitterUserId, cancellationToken);
        if (submitter == null || submitter.IsDeleted || submitter.Status != AccountStatus.Active)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            storyId = context.Story.Id,
            reason = context.Reason.ToString()
        });
        await _notificationService.CreateAsync(
            submitter.Id, NotificationType.StoryArchivedByModeration, payload, cancellationToken);
    }
}
