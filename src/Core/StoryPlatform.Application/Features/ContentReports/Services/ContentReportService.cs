using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ContentReports.DTOs;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentReports.Services;

public class ContentReportService : IContentReportService
{
    private const int MaxDescriptionLength = 2000;
    private const int MaxAttachmentUrlLength = 500;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IReportableStoryResolver _storyResolver;
    private readonly IModerationQueueService _queueService;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INotificationService _notificationService;

    public ContentReportService(
        IUnitOfWork unitOfWork,
        IReportableStoryResolver storyResolver,
        IModerationQueueService queueService,
        IAuditLogWriter auditLogWriter,
        INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _storyResolver = storyResolver;
        _queueService = queueService;
        _auditLogWriter = auditLogWriter;
        _notificationService = notificationService;
    }

    public async Task<ContentReportResultDto> CreateAsync(
        int reporterUserId,
        CreateContentReportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var reporter = await RequireParentAsync(reporterUserId, cancellationToken);
        if (!Enum.IsDefined(request.Reason))
        {
            throw new BadRequestException("Lý do báo cáo không hợp lệ.");
        }

        var description = NormalizeDescription(request.Description);
        var attachmentUrl = NormalizeAttachmentUrl(request.AttachmentUrl);
        var reportable = await _storyResolver.ResolveAsync(reporter.Id, request.StoryId, cancellationToken);

        var reportRepository = _unitOfWork.Repository<ContentReport>();
        var alreadyPending = await reportRepository.ExistsAsync(
            report => report.StoryId == reportable.Story.Id
                      && report.ReporterUserId == reporter.Id
                      && report.Status == ContentReportStatus.Pending
                      && !report.IsReviewItem,
            cancellationToken);
        if (alreadyPending)
        {
            throw new ConflictException("Bạn đã báo cáo truyện này và báo cáo đang chờ xử lý.");
        }

        ContentReport created;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            created = new ContentReport
            {
                StoryId = reportable.Story.Id,
                ReporterUserId = reporter.Id,
                Reason = request.Reason,
                Description = description,
                AttachmentUrl = attachmentUrl,
                StoryVersionId = reportable.DisplayedVersionId,
                IsReviewItem = false,
                Status = ContentReportStatus.Pending
            };
            await reportRepository.AddAsync(created, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var item = await _queueService.EnqueueAsync(new EnqueueModerationItemCommand
            {
                ItemType = ModerationItemType.ContentReport,
                StoryId = reportable.Story.Id,
                StoryVersionId = reportable.DisplayedVersionId,
                ContentReportId = created.Id,
                CreatedByUserId = reporter.Id,
                IsSafetyConcern = request.Reason == ContentReportReason.SafetyConcern
            }, cancellationToken);

            await _auditLogWriter.LogAsync(
                reporter.Id,
                "ContentReportCreated",
                nameof(ContentReport),
                created.Id,
                beforeState: null,
                afterState: new
                {
                    storyId = reportable.Story.Id,
                    storyVersionId = reportable.DisplayedVersionId,
                    reason = request.Reason.ToString()
                },
                cancellationToken: cancellationToken);

            await NotifyStaffAsync(created, item, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return new ContentReportResultDto
        {
            ReportId = created.Id,
            StoryId = created.StoryId,
            Reason = created.Reason.ToString(),
            Status = created.Status.ToString(),
            CreatedAt = created.CreatedAt
        };
    }

    private async Task<UserAccount> RequireParentAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken);
        if (user == null || user.IsDeleted || user.Status != AccountStatus.Active || user.Role != UserRole.Parent)
        {
            throw new ForbiddenException(
                "Chỉ người giám sát (phụ huynh) đang hoạt động mới báo cáo được truyện.");
        }

        return user;
    }

    private async Task NotifyStaffAsync(
        ContentReport report, ModerationItem item, CancellationToken cancellationToken)
    {
        var userRepository = _unitOfWork.Repository<UserAccount>();
        var recipients = await userRepository.FindAsync(
            user => user.Role == UserRole.Moderator
                    && user.Status == AccountStatus.Active
                    && !user.IsDeleted,
            cancellationToken: cancellationToken);
        if (recipients.Count == 0)
        {
            recipients = await userRepository.FindAsync(
                user => user.Role == UserRole.Administrator
                        && user.Status == AccountStatus.Active
                        && !user.IsDeleted,
                cancellationToken: cancellationToken);
        }

        if (recipients.Count == 0)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            contentReportId = report.Id,
            moderationItemId = item.Id,
            storyId = report.StoryId,
            reason = report.Reason.ToString()
        });
        foreach (var recipient in recipients)
        {
            await _notificationService.CreateAsync(
                recipient.Id, NotificationType.ContentReported, payload, cancellationToken);
        }
    }

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > MaxDescriptionLength)
        {
            throw new BadRequestException($"Mô tả tối đa {MaxDescriptionLength} ký tự.");
        }

        return trimmed;
    }

    private static string? NormalizeAttachmentUrl(string? attachmentUrl)
    {
        var trimmed = attachmentUrl?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > MaxAttachmentUrlLength)
        {
            throw new BadRequestException($"Nguồn đính kèm tối đa {MaxAttachmentUrlLength} ký tự.");
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new BadRequestException("Nguồn đính kèm phải là đường dẫn http hoặc https hợp lệ.");
        }

        return trimmed;
    }
}
