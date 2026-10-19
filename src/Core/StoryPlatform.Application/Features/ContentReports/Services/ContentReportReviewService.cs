using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ContentReports.DTOs;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
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

    public ContentReportReviewService(
        IUnitOfWork unitOfWork,
        IAuditLogWriter auditLogWriter,
        INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _notificationService = notificationService;
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
            CreatedAt = report.CreatedAt,
            Story = new ReportedStoryDto
            {
                StoryId = story.Id,
                Title = Mask(story.Title),
                Description = Mask(story.Description),
                Genre = story.Genre,
                AgeBand = story.AgeBand,
                ReadingLevel = story.ReadingLevel,
                Language = story.Language,
                Scope = story.Scope.ToString(),
                IsArchived = story.Status == StoryStatus.Archived || story.ArchivedAt != null,
                Labels = await LoadLabelsAsync(story.Id, cancellationToken)
            }
        };

        if (version != null && snapshot != null)
        {
            detail.Version = new ReportedVersionDto
            {
                VersionId = version.Id,
                VersionNo = version.VersionNo,
                Title = Mask(version.Title) ?? string.Empty,
                Content = Mask(version.Content),
                Lesson = Mask(version.Lesson)
            };
            detail.Scenes = snapshot.Scenes.Select(scene => new ReportedSceneDto
            {
                SceneIndex = scene.SceneIndex,
                Text = Mask(scene.SceneText) ?? string.Empty,
                ImageUrls = snapshot.ImageUrls(scene.Id).ToList(),
                AudioUrls = snapshot.AudioUrls(scene.Id).ToList()
            }).ToList();
            detail.Vocabulary = snapshot.Vocabulary.Select(word => new ReportedVocabularyDto
            {
                Term = Mask(word.Term) ?? string.Empty,
                Definition = Mask(word.Definition) ?? string.Empty
            }).ToList();
            detail.Quizzes = snapshot.Quizzes.Select(quiz => new ReportedQuizDto
            {
                Type = quiz.Type.ToString(),
                Question = Mask(quiz.Question) ?? string.Empty,
                CorrectAnswer = Mask(quiz.CorrectAnswer),
                Choices = Mask(quiz.Choices)
            }).ToList();
            detail.DiscussionQuestions = snapshot.Discussions.Select(question => new ReportedDiscussionDto
            {
                Question = Mask(question.Question) ?? string.Empty,
                IsMoralLesson = question.IsMoralLesson
            }).ToList();
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

    /// <summary>
    /// Cổng chung cho mọi thao tác trên mục report: nhân sự đang hoạt động, mục đang do chính mình nhận,
    /// chưa quá thời hạn claim, report còn Pending và truyện còn tồn tại.
    /// </summary>
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

        if (report.Status != ContentReportStatus.Pending)
        {
            throw new ConflictException("Report này đã được xử lý.");
        }

        var story = await _unitOfWork.Repository<Story>()
            .GetByIdAsync(report.StoryId, cancellationToken);
        if (story == null || story.IsDeleted)
        {
            throw new NotFoundException("Truyện", report.StoryId);
        }

        return new ReportContext(actor, item, report, story);
    }

    private async Task<List<ReportedLabelDto>> LoadLabelsAsync(
        int storyId, CancellationToken cancellationToken)
    {
        var links = await _unitOfWork.Repository<StoryCategory>().FindAsync(
            link => link.StoryId == storyId,
            cancellationToken: cancellationToken);
        var categoryIds = links.Select(link => link.ContentCategoryId).ToList();
        var categories = await _unitOfWork.Repository<ContentCategory>().FindAsync(
            category => categoryIds.Contains(category.Id),
            cancellationToken: cancellationToken);

        return categories.OrderBy(category => category.Id)
            .Select(category => new ReportedLabelDto
            {
                Code = category.Code,
                DisplayName = category.DisplayName
            })
            .ToList();
    }
}
