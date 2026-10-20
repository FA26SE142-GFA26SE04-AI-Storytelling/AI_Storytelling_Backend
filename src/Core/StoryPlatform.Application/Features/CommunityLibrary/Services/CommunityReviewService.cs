using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.CommunityLibrary.Interfaces;
using StoryPlatform.Application.Features.ContentReports;
using StoryPlatform.Application.Features.ContentReports.DTOs;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.CommunityLibrary.Services;

public partial class CommunityReviewService : ICommunityReviewService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INotificationService _notificationService;

    public CommunityReviewService(
        IUnitOfWork unitOfWork, IAuditLogWriter auditLogWriter, INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _notificationService = notificationService;
    }

    private sealed record ReviewContext(
        UserAccount Actor, ModerationItem Item, Story Story, StoryVersion Version, ChildProfile? SourceChild);

    public async Task<CommunityReviewPackageDto> GetPackageAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var nickname = context.SourceChild?.Nickname;
        string? Mask(string? text) => ModeratorTextMasker.Mask(text, nickname);

        var snapshot = await PublicVersionSnapshot.LoadAsync(_unitOfWork, context.Version, cancellationToken);
        var labels = await LoadLabelsAsync(context.Story.Id, cancellationToken);
        var findings = await CommunityPiiScanner.ScanAsync(
            _unitOfWork, context.Story, context.Version, nickname, cancellationToken);
        var checklist = CommunityReviewState.ReadChecklist(context.Item.ReviewProgress);
        var blockers = EvaluateBlockers(context, checklist, findings, labels.Count);

        var package = new CommunityReviewPackageDto
        {
            ItemId = context.Item.Id,
            Story = ReportedContentBuilder.BuildStory(
                context.Story,
                labels.Select(label => new ReportedLabelDto { Code = label.Code, DisplayName = label.DisplayName }).ToList(),
                Mask),
            Version = ReportedContentBuilder.BuildVersion(context.Version, Mask),
            Scenes = ReportedContentBuilder.BuildScenes(snapshot, Mask),
            Vocabulary = ReportedContentBuilder.BuildVocabulary(snapshot, Mask),
            Quizzes = ReportedContentBuilder.BuildQuizzes(snapshot, Mask),
            DiscussionQuestions = ReportedContentBuilder.BuildDiscussions(snapshot, Mask),
            Labels = labels,
            CopyrightDeclaration = context.Item.CopyrightDeclaration?.ToString(),
            PiiCheckSummary = context.Item.PiiCheckResult,
            PiiRescan = findings.Select(finding => new CommunityPiiLocationDto
            {
                Kind = finding.Kind, Location = finding.Location
            }).ToList(),
            IsResubmission = context.Item.IsResubmission,
            Checklist = checklist,
            Blockers = blockers,
            CanApprove = blockers.Count == 0,
            ClaimExpiresAt = context.Item.ClaimedAt!.Value.Add(ModerationQueuePolicy.ClaimTimeout)
        };

        await _auditLogWriter.LogAsync(
            context.Actor.Id, "CommunityReviewPackageOpened", nameof(ModerationItem), context.Item.Id,
            beforeState: null,
            afterState: new { storyId = context.Story.Id, storyVersionId = context.Version.Id },
            cancellationToken: cancellationToken);

        return package;
    }

    public async Task<List<CommunityChecklistEntryDto>> SaveChecklistAsync(
        int actorUserId, int itemId, SaveCommunityChecklistRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);

        var entries = request.Entries ?? new List<CommunityChecklistEntryDto>();
        if (entries.Count == 0)
        {
            throw new BadRequestException("Phải gửi ít nhất một tiêu chí.");
        }

        var codes = entries.Select(entry => entry.Code).ToList();
        if (codes.Any(code => !CommunityReviewPolicy.CriterionCodes.Contains(code)))
        {
            throw new BadRequestException(
                "Có tiêu chí không hợp lệ (safety_baseline, no_personal_info, metadata_labels).");
        }

        if (codes.Distinct().Count() != codes.Count)
        {
            throw new BadRequestException("Mỗi tiêu chí chỉ được gửi một lần.");
        }

        if (entries.Any(entry => entry.Note != null && entry.Note.Trim().Length > CommunityReviewPolicy.MaxNoteLength))
        {
            throw new BadRequestException($"Ghi chú tối đa {CommunityReviewPolicy.MaxNoteLength} ký tự.");
        }

        var checklist = CommunityReviewState.ReadChecklist(context.Item.ReviewProgress);
        var before = checklist.Select(entry => new { entry.Code, entry.Passed }).ToList();
        foreach (var entry in entries)
        {
            var target = checklist.Single(candidate => candidate.Code == entry.Code);
            target.Passed = entry.Passed;
            var note = entry.Note?.Trim();
            target.Note = string.IsNullOrEmpty(note) ? null : note;
        }

        context.Item.ReviewProgress = CommunityReviewState.WriteChecklist(checklist);
        await SaveItemAsync(context.Item, cancellationToken);

        await _auditLogWriter.LogAsync(
            context.Actor.Id, "CommunityReviewChecklistSaved", nameof(ModerationItem), context.Item.Id,
            beforeState: before,
            afterState: checklist.Select(entry => new { entry.Code, entry.Passed }).ToList(),
            cancellationToken: cancellationToken);

        return checklist;
    }

    public async Task<List<CommunityLabelDto>> SetLabelsAsync(
        int actorUserId, int itemId, SetCommunityLabelsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var wanted = (request.ContentCategoryIds ?? new List<int>()).Distinct().ToList();
        if (wanted.Count == 0)
        {
            throw new BadRequestException("Truyện phải có ít nhất một nhãn nội dung.");
        }

        var categories = await _unitOfWork.Repository<ContentCategory>().FindAsync(
            category => wanted.Contains(category.Id), cancellationToken: cancellationToken);
        var invalid = wanted.Where(id => !categories.Any(category => category.Id == id && category.IsActive)).ToList();
        if (invalid.Count > 0)
        {
            throw new BadRequestException(
                $"Nhãn không tồn tại hoặc không còn hoạt động: {string.Join(", ", invalid)}.");
        }

        var before = await LoadLabelsAsync(context.Story.Id, cancellationToken);
        var repository = _unitOfWork.Repository<StoryCategory>();
        var storyId = context.Story.Id;
        var existing = await repository.FindAsync(link => link.StoryId == storyId, cancellationToken: cancellationToken);

        foreach (var link in existing.Where(link => !wanted.Contains(link.ContentCategoryId)))
        {
            repository.Delete(link);
        }

        foreach (var categoryId in wanted.Where(id => !existing.Any(link => link.ContentCategoryId == id)))
        {
            await repository.AddAsync(new StoryCategory
            {
                StoryId = storyId, ContentCategoryId = categoryId, Source = TagSource.Moderator
            }, cancellationToken);
        }

        await SaveItemAsync(context.Item, cancellationToken);

        var after = await LoadLabelsAsync(storyId, cancellationToken);
        await _auditLogWriter.LogAsync(
            context.Actor.Id, "CommunityReviewLabelsChanged", nameof(ModerationItem), context.Item.Id,
            beforeState: new { labels = before.Select(label => label.Id) },
            afterState: new { labels = after.Select(label => label.Id) },
            cancellationToken: cancellationToken);

        return after;
    }

    /// <summary>Nạp mục do chính người gọi đang giữ (trong 2 giờ) cùng truyện, bản đã ghim và bé nguồn.</summary>
    private async Task<ReviewContext> LoadClaimedContextAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken)
    {
        var actor = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(actorUserId, cancellationToken);
        if (actor == null
            || actor.IsDeleted
            || actor.Status != AccountStatus.Active
            || actor.Role is not (UserRole.Moderator or UserRole.Administrator))
        {
            throw new ForbiddenException(
                "Chỉ Moderator hoặc Administrator đang hoạt động mới duyệt được truyện Community.");
        }

        var item = await _unitOfWork.Repository<ModerationItem>().GetByIdAsync(itemId, cancellationToken)
                   ?? throw new NotFoundException("Mục kiểm duyệt", itemId);
        if (item.ItemType != ModerationItemType.CommunitySubmission)
        {
            throw new BadRequestException("Mục này không phải mục duyệt truyện Community.");
        }

        if (item.Status != ModerationItemStatus.Claimed || item.ClaimedByUserId != actor.Id)
        {
            throw new ForbiddenException("Bạn chỉ được thao tác trên mục đang do chính mình nhận.");
        }

        if (item.ClaimedAt == null || item.ClaimedAt.Value.Add(ModerationQueuePolicy.ClaimTimeout) <= DateTime.UtcNow)
        {
            throw new ForbiddenException("Lượt nhận mục đã quá 2 giờ; hãy nhận lại mục từ hàng đợi.");
        }

        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(item.StoryId ?? 0, cancellationToken);
        if (story == null || story.IsDeleted)
        {
            throw new NotFoundException("Truyện", item.StoryId ?? 0);
        }

        var version = await _unitOfWork.Repository<StoryVersion>()
            .GetByIdAsync(item.StoryVersionId ?? 0, cancellationToken);
        if (version == null || version.IsDeleted || version.StoryId != story.Id)
        {
            throw new NotFoundException("Phiên bản truyện", item.StoryVersionId ?? 0);
        }

        var child = item.SourceChildProfileId.HasValue
            ? await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(item.SourceChildProfileId.Value, cancellationToken)
            : null;
        return new ReviewContext(actor, item, story, version, child);
    }

    private static List<CommunityReviewBlockerDto> EvaluateBlockers(
        ReviewContext context,
        IReadOnlyList<CommunityChecklistEntryDto> checklist,
        IReadOnlyList<PiiFinding> findings,
        int labelCount)
    {
        var blockers = new List<CommunityReviewBlockerDto>();

        if (context.Item.CreatedByUserId == context.Actor.Id)
        {
            blockers.Add(Blocker(CommunityReviewBlockerCodes.OwnSubmission,
                "Bạn không thể duyệt truyện do chính mình nộp."));
        }

        var notPassed = checklist.Where(entry => !entry.Passed).ToList();
        if (notPassed.Count > 0)
        {
            blockers.Add(Blocker(CommunityReviewBlockerCodes.ChecklistIncomplete,
                "Checklist chưa đạt đủ 3 tiêu chí (còn: "
                + string.Join("; ", notPassed.Select(entry => CommunityReviewPolicy.CriterionLabel(entry.Code)))
                + "). Nếu truyện chưa đạt, hãy chọn Reject."));
        }

        if (context.Story.Status == StoryStatus.Archived || context.Story.ArchivedAt != null)
        {
            blockers.Add(Blocker(CommunityReviewBlockerCodes.StoryArchived,
                "Truyện gốc đã bị lưu trữ nên không thể Approve."));
        }

        if (findings.Count > 0)
        {
            blockers.Add(Blocker(CommunityReviewBlockerCodes.PiiFound,
                "Bản đã ghim vẫn còn thông tin có thể nhận ra bé hoặc gia đình; hãy Reject."));
        }

        if (labelCount == 0)
        {
            blockers.Add(Blocker(CommunityReviewBlockerCodes.NoLabels,
                "Truyện chưa có nhãn nội dung; hãy gắn nhãn trước khi Approve."));
        }

        return blockers;
    }

    private static CommunityReviewBlockerDto Blocker(string code, string message) =>
        new() { Code = code, Message = message };

    private async Task<List<CommunityLabelDto>> LoadLabelsAsync(int storyId, CancellationToken cancellationToken)
    {
        var links = await _unitOfWork.Repository<StoryCategory>().FindAsync(
            link => link.StoryId == storyId, cancellationToken: cancellationToken);
        var categoryIds = links.Select(link => link.ContentCategoryId).Distinct().ToList();
        var categories = await _unitOfWork.Repository<ContentCategory>().FindAsync(
            category => categoryIds.Contains(category.Id), cancellationToken: cancellationToken);
        return categories.OrderBy(category => category.Id).Select(category => new CommunityLabelDto
        {
            Id = category.Id, Code = category.Code, DisplayName = category.DisplayName
        }).ToList();
    }

    private async Task SaveItemAsync(ModerationItem item, CancellationToken cancellationToken)
    {
        item.RowVersion += 1;
        _unitOfWork.Repository<ModerationItem>().Update(item);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
