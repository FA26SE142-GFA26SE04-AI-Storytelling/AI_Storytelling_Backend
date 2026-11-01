using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ContentReports.DTOs;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Application.Features.ContentReports;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentReports.Services;

public partial class ContentReportReviewService : IContentReportReviewService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INotificationService _notificationService;
    private readonly IStoryArchiveConsequences _archiveConsequences;

    public ContentReportReviewService(
        IUnitOfWork unitOfWork,
        IAuditLogWriter auditLogWriter,
        INotificationService notificationService,
        IStoryArchiveConsequences archiveConsequences)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _notificationService = notificationService;
        _archiveConsequences = archiveConsequences;
    }

    private sealed record ReportContext(
        UserAccount Actor, ModerationItem Item, ContentReport Report, Story Story);

    public async Task<ContentReportDetailDto> GetDetailAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var story = context.Story;
        var report = context.Report;

        string? nickname = null;
        if (story.ChildProfileId.HasValue)
        {
            var child = await _unitOfWork.Repository<ChildProfile>()
                .GetByIdAsync(story.ChildProfileId.Value, cancellationToken);
            nickname = child?.Nickname;
        }

        string? Mask(string? text) => ModeratorTextMasker.Mask(text, nickname);

        StoryVersion? version = null;
        if (report.StoryVersionId.HasValue)
        {
            var candidate = await _unitOfWork.Repository<StoryVersion>()
                .GetByIdAsync(report.StoryVersionId.Value, cancellationToken);
            if (candidate != null && !candidate.IsDeleted && candidate.StoryId == story.Id)
            {
                version = candidate;
            }
        }

        var snapshot = version == null
            ? null
            : await PublicVersionSnapshot.LoadAsync(_unitOfWork, version, cancellationToken);

        var detail = new ContentReportDetailDto
        {
            ItemId = context.Item.Id,
            ReportId = report.Id,
            Reason = report.Reason.ToString(),
            Description = Mask(report.Description),
            AttachmentUrl = report.AttachmentUrl,
            IsReviewItem = report.IsReviewItem,
            ItemStatus = context.Item.Status.ToString(),
            EscalationReason = Mask(context.Item.EscalationReason),
            EscalatedAt = context.Item.EscalatedAt,
            CreatedAt = report.CreatedAt,
            Story = ReportedContentBuilder.BuildStory(
                story,
                await ReportedContentBuilder.LoadLabelsAsync(_unitOfWork, story.Id, cancellationToken),
                Mask)
        };

        if (version != null && snapshot != null)
        {
            detail.Version = ReportedContentBuilder.BuildVersion(version, Mask);
            detail.Scenes = ReportedContentBuilder.BuildScenes(snapshot, Mask);
            detail.Vocabulary = ReportedContentBuilder.BuildVocabulary(snapshot, Mask);
            detail.Quizzes = ReportedContentBuilder.BuildQuizzes(snapshot, Mask);
            detail.Listening = ReportedContentBuilder.BuildListening(snapshot, Mask);
        }

        await _auditLogWriter.LogAsync(
            context.Actor.Id,
            "ContentReportOpened",
            nameof(ModerationItem),
            context.Item.Id,
            beforeState: null,
            afterState: new { reportId = report.Id, storyId = story.Id, storyVersionId = version?.Id },
            cancellationToken: cancellationToken);

        return detail;
    }

    /// <summary>Cổng chung cho mục đang do chính mình nhận hoặc ca Escalated do Administrator xử lý.</summary>
    private async Task<ReportContext> LoadClaimedContextAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken)
    {
        var actor = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(actorUserId, cancellationToken);
        if (actor == null
            || actor.IsDeleted
            || actor.Status != AccountStatus.Active
            || actor.Role is not (UserRole.Moderator or UserRole.Administrator))
        {
            throw new ForbiddenException(
                "Chỉ Moderator hoặc Administrator đang hoạt động mới xử lý được report.");
        }

        var item = await _unitOfWork.Repository<ModerationItem>()
            .GetByIdAsync(itemId, cancellationToken);
        if (item == null || item.IsDeleted)
        {
            throw new NotFoundException("Mục kiểm duyệt", itemId);
        }

        if (item.ItemType != ModerationItemType.ContentReport)
        {
            throw new BadRequestException("Mục này không phải mục xử lý report.");
        }

        if (item.Status == ModerationItemStatus.Escalated)
        {
            if (actor.Role != UserRole.Administrator)
            {
                throw new ForbiddenException("Ca đã chuyển cho Administrator quyết định.");
            }

            return await BuildContextAsync(actor, item, ContentReportStatus.Escalated, cancellationToken);
        }

        if (item.Status != ModerationItemStatus.Claimed || item.ClaimedByUserId != actor.Id)
        {
            throw new ForbiddenException("Bạn chỉ được đọc mục đang do chính mình nhận.");
        }

        if (item.ClaimedAt == null
            || item.ClaimedAt.Value.Add(ModerationQueuePolicy.ClaimTimeout) <= DateTime.UtcNow)
        {
            throw new ConflictException(
                "Mục đã quá 2 giờ kể từ lúc nhận và sẽ được trả về hàng đợi. Vui lòng nhận lại.");
        }

        return await BuildContextAsync(actor, item, ContentReportStatus.Pending, cancellationToken);
    }

    private async Task<ReportContext> BuildContextAsync(
        UserAccount actor, ModerationItem item, ContentReportStatus expectedReportStatus,
        CancellationToken cancellationToken)
    {
        if (item.ContentReportId == null)
        {
            throw new BadRequestException("Mục chưa gắn report để xử lý.");
        }

        var report = await _unitOfWork.Repository<ContentReport>()
            .GetByIdAsync(item.ContentReportId.Value, cancellationToken);
        if (report == null || report.IsDeleted)
        {
            throw new NotFoundException("Report", item.ContentReportId.Value);
        }

        if (report.Status != expectedReportStatus)
        {
            throw new ConflictException("Report này đã được xử lý.");
        }

        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(report.StoryId, cancellationToken);
        if (story == null || story.IsDeleted)
        {
            throw new NotFoundException("Truyện", report.StoryId);
        }

        return new ReportContext(actor, item, report, story);
    }
}
