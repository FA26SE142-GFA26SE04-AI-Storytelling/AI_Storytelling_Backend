using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ContentReports.DTOs;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Application.Features.ContentReports;
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

            await NotifyStaffAsync(created, item, null, cancellationToken);
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

    public async Task<ContentReportResultDto> CreateReviewItemAsync(
        int moderatorUserId, CreateReviewItemRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var moderator = await RequireModeratorAsync(moderatorUserId, cancellationToken);
        if (!Enum.IsDefined(request.Reason))
        {
            throw new BadRequestException("Lý do báo cáo không hợp lệ.");
        }

        var description = NormalizeDescription(request.Description);
        var attachmentUrl = NormalizeAttachmentUrl(request.AttachmentUrl);
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(request.StoryId, cancellationToken);
        if (story == null || story.IsDeleted)
        {
            throw new NotFoundException("Truyện", request.StoryId);
        }

        if (story.Scope != StoryScope.Creator || !story.IsPublished
            || story.Status == StoryStatus.Archived || story.ArchivedAt != null)
        {
            throw new BadRequestException("Chỉ tạo mục rà soát cho nội dung Public Library đang công khai.");
        }

        if (request.ReadingRoadmapId.HasValue)
        {
            var roadmap = await _unitOfWork.Repository<ReadingRoadmap>()
                .GetByIdAsync(request.ReadingRoadmapId.Value, cancellationToken);
            if (roadmap == null || roadmap.IsDeleted)
            {
                throw new NotFoundException("Bộ truyện mẫu", request.ReadingRoadmapId.Value);
            }

            if (roadmap.Status != RoadmapTemplateStatus.Published || roadmap.ArchivedAt != null)
            {
                throw new BadRequestException("Bộ truyện mẫu không còn công khai.");
            }

            var roadmapId = roadmap.Id;
            var storyId = story.Id;
            var inRoadmap = await _unitOfWork.Repository<ReadingRoadmapItem>().ExistsAsync(
                item => item.ReadingRoadmapId == roadmapId && item.StoryId == storyId,
                cancellationToken);
            if (!inRoadmap)
            {
                throw new BadRequestException("Truyện không thuộc bộ truyện mẫu này.");
            }
        }

        var reportRepository = _unitOfWork.Repository<ContentReport>();
        var alreadyPending = await reportRepository.ExistsAsync(
            report => report.StoryId == story.Id
                      && report.ReporterUserId == moderator.Id
                      && report.Status == ContentReportStatus.Pending
                      && report.IsReviewItem,
            cancellationToken);
        if (alreadyPending)
        {
            throw new ConflictException("Bạn đã tạo mục rà soát cho truyện này và mục đang chờ xử lý.");
        }

        var displayedVersion = await ReportedContentBuilder.FindDisplayedVersionAsync(
            _unitOfWork, story, cancellationToken);
        ContentReport created;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            created = new ContentReport
            {
                StoryId = story.Id,
                ReporterUserId = moderator.Id,
                Reason = request.Reason,
                Description = description,
                AttachmentUrl = attachmentUrl,
                StoryVersionId = displayedVersion?.Id,
                ReadingRoadmapId = request.ReadingRoadmapId,
                IsReviewItem = true,
                Status = ContentReportStatus.Pending
            };
            await reportRepository.AddAsync(created, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var item = await _queueService.EnqueueAsync(new EnqueueModerationItemCommand
            {
                ItemType = ModerationItemType.ContentReport,
                StoryId = story.Id,
                StoryVersionId = displayedVersion?.Id,
                ReadingRoadmapId = request.ReadingRoadmapId,
                ContentReportId = created.Id,
                CreatedByUserId = moderator.Id,
                IsSafetyConcern = request.Reason == ContentReportReason.SafetyConcern
            }, cancellationToken);

            await _auditLogWriter.LogAsync(
                moderator.Id, "ContentReviewItemCreated", nameof(ContentReport), created.Id,
                beforeState: null,
                afterState: new
                {
                    storyId = story.Id,
                    storyVersionId = displayedVersion?.Id,
                    readingRoadmapId = request.ReadingRoadmapId,
                    reason = request.Reason.ToString()
                },
                cancellationToken: cancellationToken);

            await NotifyStaffAsync(created, item, moderator.Id, cancellationToken);
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

    private async Task<UserAccount> RequireModeratorAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken);
        if (user == null || user.IsDeleted || user.Status != AccountStatus.Active || user.Role != UserRole.Moderator)
        {
            throw new ForbiddenException("Chỉ Moderator đang hoạt động mới tạo được mục rà soát.");
        }

        return user;
    }

    private async Task NotifyStaffAsync(
        ContentReport report, ModerationItem item, int? excludeUserId, CancellationToken cancellationToken)
    {
        var excluded = excludeUserId ?? 0;
        var userRepository = _unitOfWork.Repository<UserAccount>();
        var recipients = await userRepository.FindAsync(
            user => user.Role == UserRole.Moderator
                    && user.Status == AccountStatus.Active
                    && !user.IsDeleted
                    && user.Id != excluded,
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
