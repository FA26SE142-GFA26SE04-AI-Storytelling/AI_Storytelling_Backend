using System.Text.Json;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentReports.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentReports.Services;

// Escalate: Moderator chuyển ca cho Administrator quyết định cuối.
public partial class ContentReportReviewService
{
    public async Task<ContentReportDecisionResultDto> EscalateAsync(
        int actorUserId, int itemId, EscalateReportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        if (context.Actor.Role != UserRole.Moderator)
        {
            throw new ForbiddenException("Chỉ Moderator mới chuyển ca cho Administrator.");
        }

        var reason = RequireReason(request.Reason);
        var administrators = await _unitOfWork.Repository<UserAccount>().FindAsync(
            user => user.Role == UserRole.Administrator
                    && user.Status == AccountStatus.Active
                    && !user.IsDeleted,
            cancellationToken: cancellationToken);
        if (administrators.Count == 0)
        {
            throw new ConflictException("Hiện không có Administrator đang hoạt động để nhận ca này.");
        }

        var item = context.Item;
        var report = context.Report;
        var now = DateTime.UtcNow;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            item.Status = ModerationItemStatus.Escalated;
            item.EscalationReason = reason;
            item.EscalatedAt = now;
            item.RowVersion += 1;
            _unitOfWork.Repository<ModerationItem>().Update(item);

            report.Status = ContentReportStatus.Escalated;
            report.ReviewedByUserId = context.Actor.Id;
            report.ReviewedAt = now;
            _unitOfWork.Repository<ContentReport>().Update(report);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _auditLogWriter.LogAsync(
                context.Actor.Id,
                "ContentReportEscalated",
                nameof(ModerationItem),
                item.Id,
                beforeState: new { status = ModerationItemStatus.Claimed.ToString() },
                afterState: new
                {
                    status = ModerationItemStatus.Escalated.ToString(),
                    reportId = report.Id,
                    storyId = context.Story.Id,
                    reason
                },
                cancellationToken: cancellationToken);

            var payload = JsonSerializer.Serialize(new
            {
                contentReportId = report.Id,
                moderationItemId = item.Id,
                storyId = context.Story.Id,
                reason = report.Reason.ToString()
            });
            foreach (var administrator in administrators)
            {
                await _notificationService.CreateAsync(
                    administrator.Id, NotificationType.ContentReportEscalated, payload, cancellationToken);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return new ContentReportDecisionResultDto
        {
            ItemId = item.Id,
            ReportId = report.Id,
            StoryId = context.Story.Id,
            Decision = ModerationItemStatus.Escalated.ToString(),
            StoryArchived = context.Story.ArchivedAt != null,
            DecidedAt = now
        };
    }
}
