using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentReports.Services;

public class PendingSubmissionArchiveHandler : IStoryArchiveHandler
{
    public const string ClosedReason = "story_archived";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INotificationService _notificationService;

    public PendingSubmissionArchiveHandler(
        IUnitOfWork unitOfWork,
        IAuditLogWriter auditLogWriter,
        INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _notificationService = notificationService;
    }

    public async Task HandleAsync(
        StoryArchiveContext context, CancellationToken cancellationToken = default)
    {
        var storyId = context.Story.Id;
        var itemRepository = _unitOfWork.Repository<ModerationItem>();
        var open = await itemRepository.FindAsync(
            item => item.ItemType == ModerationItemType.PublicStoryReview
                    && item.StoryId == storyId
                    && (item.Status == ModerationItemStatus.Pending
                        || item.Status == ModerationItemStatus.Claimed),
            cancellationToken: cancellationToken);
        if (open.Count == 0)
        {
            return;
        }

        var closed = new List<(ModerationItem Item, ModerationItemStatus Before, int? ClaimedBy)>();
        foreach (var candidate in open)
        {
            var item = await itemRepository.GetByIdAsync(candidate.Id, cancellationToken);
            if (item == null || item.IsDeleted
                || item.Status is not (ModerationItemStatus.Pending or ModerationItemStatus.Claimed))
            {
                continue;
            }

            var before = item.Status;
            var claimedBy = item.ClaimedByUserId;
            if (before == ModerationItemStatus.Pending)
            {
                item.Status = ModerationItemStatus.Withdrawn;
                item.WithdrawnAt = context.Now;
            }
            else
            {
                item.Status = ModerationItemStatus.Closed;
                item.DecidedAt = context.Now;
                item.ClosedReason = ClosedReason;
            }

            item.RowVersion += 1;
            itemRepository.Update(item);

            if (item.StoryVersionId.HasValue)
            {
                var version = await _unitOfWork.Repository<StoryVersion>()
                    .GetByIdAsync(item.StoryVersionId.Value, cancellationToken);
                if (version != null && version.Moderation.ReviewStatus == VersionReviewStatus.Pending)
                {
                    version.Moderation.ReviewStatus = VersionReviewStatus.None;
                    version.Moderation.ReviewSubmittedAt = null;
                    _unitOfWork.Repository<StoryVersion>().Update(version);
                }
            }

            closed.Add((item, before, claimedBy));
        }

        if (closed.Count == 0)
        {
            return;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var (item, before, claimedBy) in closed)
        {
            await _auditLogWriter.LogAsync(
                context.ActorUserId,
                "PublicStorySubmissionClosedByArchive",
                nameof(ModerationItem),
                item.Id,
                beforeState: new { status = before.ToString() },
                afterState: new
                {
                    status = item.Status.ToString(),
                    storyId,
                    storyVersionId = item.StoryVersionId,
                    archivedReason = context.Reason.ToString()
                },
                cancellationToken: cancellationToken);

            if (before == ModerationItemStatus.Claimed && claimedBy.HasValue)
            {
                await NotifyClaimantAsync(item, storyId, claimedBy.Value, cancellationToken);
            }
        }
    }

    private async Task NotifyClaimantAsync(
        ModerationItem item,
        int storyId,
        int claimantUserId,
        CancellationToken cancellationToken)
    {
        var claimant = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(claimantUserId, cancellationToken);
        if (claimant == null || claimant.IsDeleted || claimant.Status != AccountStatus.Active)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            itemId = item.Id,
            itemType = ModerationItemType.PublicStoryReview.ToString(),
            storyId,
            decision = "closed",
            reason = ClosedReason
        });
        await _notificationService.CreateAsync(
            claimant.Id, NotificationType.ContentModerationResult, payload, cancellationToken);
    }
}
