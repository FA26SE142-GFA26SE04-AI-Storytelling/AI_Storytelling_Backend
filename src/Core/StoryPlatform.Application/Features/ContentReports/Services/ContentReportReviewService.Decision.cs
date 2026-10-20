using System.Text.Json;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentReports.DTOs;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentReports.Services;

// Các quyết định của Moderator trên mục report: Dismiss, Archive.
public partial class ContentReportReviewService
{
    private const int MaxReasonLength = 1000;

    public async Task<ContentReportDecisionResultDto> DismissAsync(
        int actorUserId,
        int itemId,
        DismissReportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var reason = RequireReason(request.Reason);

        return await DecideAsync(
            context,
            ModerationItemStatus.Dismissed,
            ContentReportStatus.ReviewedDismissed,
            reason,
            archiveReason: null,
            "ContentReportDismissed",
            cancellationToken);
    }

    public async Task<ContentReportDecisionResultDto> ArchiveAsync(
        int actorUserId,
        int itemId,
        ArchiveReportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);

        if (request.ArchiveReason is not (ArchivedReason.SafetyConcern
            or ArchivedReason.Copyright or ArchivedReason.Inappropriate))
        {
            throw new BadRequestException(
                "Lý do gỡ phải là SafetyConcern, Copyright hoặc Inappropriate.");
        }

        var reason = RequireReason(request.Reason);

        return await DecideAsync(
            context,
            ModerationItemStatus.Archived,
            ContentReportStatus.ReviewedArchived,
            reason,
            request.ArchiveReason,
            "ContentReportArchived",
            cancellationToken);
    }

    private static string RequireReason(string? reason)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new BadRequestException("Phải ghi lý do khi quyết định xử lý report.");
        }

        if (trimmed.Length > MaxReasonLength)
        {
            throw new BadRequestException($"Lý do tối đa {MaxReasonLength} ký tự.");
        }

        return trimmed;
    }

    private async Task<ContentReportDecisionResultDto> DecideAsync(
        ReportContext context,
        ModerationItemStatus itemStatus,
        ContentReportStatus reportStatus,
        string reason,
        ArchivedReason? archiveReason,
        string auditAction,
        CancellationToken cancellationToken)
    {
        var item = context.Item;
        var report = context.Report;
        var story = context.Story;
        var previousStatus = item.Status;
        var now = DateTime.UtcNow;
        var storyAlreadyArchived = story.Status == StoryStatus.Archived || story.ArchivedAt != null;
        var archiveNow = archiveReason.HasValue && !storyAlreadyArchived;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            item.Status = itemStatus;
            item.DecidedByUserId = context.Actor.Id;
            item.DecidedAt = now;
            item.DecisionReason = reason;
            item.RowVersion += 1;
            _unitOfWork.Repository<ModerationItem>().Update(item);

            report.Status = reportStatus;
            report.ReviewedByUserId = context.Actor.Id;
            report.ReviewedAt = now;
            report.ResolvedAt = now;
            _unitOfWork.Repository<ContentReport>().Update(report);

            if (archiveNow)
            {
                // Không xóa cứng; IsPublished giữ nguyên vì bộ lọc hiển thị kiểm trạng thái Archived.
                story.Status = StoryStatus.Archived;
                story.ArchivedReason = archiveReason!.Value;
                story.ArchivedAt = now;
                _unitOfWork.Repository<Story>().Update(story);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _auditLogWriter.LogAsync(
                context.Actor.Id,
                auditAction,
                nameof(ModerationItem),
                item.Id,
                beforeState: new { status = previousStatus.ToString() },
                afterState: new
                {
                    status = itemStatus.ToString(),
                    reportId = report.Id,
                    storyId = story.Id,
                    reason,
                    archiveReason = archiveReason?.ToString(),
                    storyArchivedNow = archiveNow
                },
                cancellationToken: cancellationToken);

            if (archiveNow)
            {
                await _archiveConsequences.ApplyAsync(
                    new StoryArchiveContext(
                        story, archiveReason!.Value, context.Actor.Id, now, report.ReporterUserId),
                    cancellationToken);
            }

            await NotifyReporterAsync(context, reportStatus, cancellationToken);
            if (archiveNow)
            {
                await NotifyStoryCreatorAsync(context, archiveReason!.Value, cancellationToken);
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
            StoryId = story.Id,
            Decision = itemStatus.ToString(),
            StoryArchived = story.Status == StoryStatus.Archived || story.ArchivedAt != null,
            DecidedAt = now
        };
    }

    private async Task NotifyReporterAsync(
        ReportContext context,
        ContentReportStatus reportStatus,
        CancellationToken cancellationToken)
    {
        var reporter = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(context.Report.ReporterUserId, cancellationToken);
        if (reporter == null || reporter.IsDeleted || reporter.Status != AccountStatus.Active)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            contentReportId = context.Report.Id,
            storyId = context.Story.Id,
            resolution = reportStatus.ToString()
        });
        await _notificationService.CreateAsync(
            reporter.Id, NotificationType.ContentReportResolved, payload, cancellationToken);
    }

    private async Task NotifyStoryCreatorAsync(
        ReportContext context,
        ArchivedReason archiveReason,
        CancellationToken cancellationToken)
    {
        var creator = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(context.Story.AuthorUserId, cancellationToken);
        if (creator == null
            || creator.IsDeleted
            || creator.Status != AccountStatus.Active
            || creator.Id == context.Report.ReporterUserId)
        {
            return;
        }

        var type = context.Story.Scope == StoryScope.Creator
            ? NotificationType.CreatorContentFlagged
            : NotificationType.StoryArchivedByModeration;
        var payload = JsonSerializer.Serialize(new
        {
            storyId = context.Story.Id,
            reason = archiveReason.ToString()
        });
        await _notificationService.CreateAsync(creator.Id, type, payload, cancellationToken);
    }
}
