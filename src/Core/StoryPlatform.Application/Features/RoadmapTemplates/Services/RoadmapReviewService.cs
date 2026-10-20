using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;
using StoryPlatform.Application.Features.RoadmapTemplates.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.RoadmapTemplates.Services;

public partial class RoadmapReviewService : IRoadmapReviewService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRoadmapSubmissionChecker _checker;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INotificationService _notificationService;

    public RoadmapReviewService(
        IUnitOfWork unitOfWork,
        IRoadmapSubmissionChecker checker,
        IAuditLogWriter auditLogWriter,
        INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _checker = checker;
        _auditLogWriter = auditLogWriter;
        _notificationService = notificationService;
    }

    private sealed record ReviewContext(
        UserAccount Actor,
        ModerationItem Item,
        ReadingRoadmap Roadmap,
        List<ReadingRoadmapItem> Items);

    public async Task<RoadmapReviewPackageDto> GetPackageAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var checks = await _checker.EvaluateAsync(context.Roadmap, context.Items, cancellationToken);
        var checklist = RoadmapReviewState.ReadChecklist(context.Item.ReviewProgress);
        var blockers = EvaluateApproveBlockers(context, checks, checklist);
        var roadmap = context.Roadmap;
        var category = await _unitOfWork.Repository<ContentCategory>()
            .GetByIdAsync(roadmap.MainCategoryId, cancellationToken);
        var creator = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(roadmap.CreatedByUserId, cancellationToken);

        var package = new RoadmapReviewPackageDto
        {
            ItemId = context.Item.Id,
            Roadmap = new RoadmapReviewInfoDto
            {
                Id = roadmap.Id,
                Name = roadmap.Name,
                Description = roadmap.Description,
                AgeBand = RoadmapAge.ToText(roadmap.AgeBand),
                MainCategory = category == null
                    ? null
                    : new RoadmapCategoryDto
                    {
                        Id = category.Id,
                        Code = category.Code,
                        DisplayName = category.DisplayName
                    },
                ReadingLevelMin = roadmap.ReadingLevelMin,
                ReadingLevelMax = roadmap.ReadingLevelMax,
                DefaultDaysPerStory = roadmap.DefaultDaysPerStory,
                CreatedByUserId = roadmap.CreatedByUserId,
                CreatedByName = creator == null ? null : DisplayName(creator),
                SubmittedAt = roadmap.SubmittedAt,
                ClonedFromId = roadmap.ClonedFromId
            },
            Stories = await RoadmapStoryLoader.LoadAsync(_unitOfWork, context.Items, cancellationToken),
            Checks = checks.Select(check => new RoadmapCheckDto
            {
                Code = check.Code,
                Passed = check.Passed,
                Message = check.Message,
                RelatedRoadmapId = check.RelatedRoadmapId
            }).ToList(),
            Checklist = checklist,
            Blockers = blockers,
            CanApprove = blockers.Count == 0,
            ClaimExpiresAt = context.Item.ClaimedAt!.Value.Add(ModerationQueuePolicy.ClaimTimeout)
        };

        await _auditLogWriter.LogAsync(
            context.Actor.Id, "RoadmapReviewPackageOpened", nameof(ModerationItem), context.Item.Id,
            beforeState: null,
            afterState: new { roadmapId = roadmap.Id },
            cancellationToken: cancellationToken);
        return package;
    }

    public async Task<List<RoadmapChecklistEntryDto>> SaveChecklistAsync(
        int actorUserId,
        int itemId,
        SaveRoadmapChecklistRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var entries = request.Entries ?? [];
        if (entries.Count == 0)
        {
            throw new BadRequestException("Phải gửi ít nhất một tiêu chí.");
        }

        var codes = entries.Select(entry => entry.Code).ToList();
        if (codes.Any(code => !RoadmapReviewPolicy.CriterionCodes.Contains(code)))
        {
            throw new BadRequestException("Có tiêu chí không hợp lệ (order, theme, name_description, age_level).");
        }

        if (codes.Distinct().Count() != codes.Count)
        {
            throw new BadRequestException("Mỗi tiêu chí chỉ được gửi một lần.");
        }

        if (entries.Any(entry => entry.Note != null
                                 && entry.Note.Trim().Length > RoadmapReviewPolicy.MaxNoteLength))
        {
            throw new BadRequestException($"Ghi chú tối đa {RoadmapReviewPolicy.MaxNoteLength} ký tự.");
        }

        var checklist = RoadmapReviewState.ReadChecklist(context.Item.ReviewProgress);
        var beforeState = checklist.Select(entry => new { entry.Code, entry.Passed }).ToList();
        foreach (var entry in entries)
        {
            var target = checklist.Single(candidate => candidate.Code == entry.Code);
            target.Passed = entry.Passed;
            var note = entry.Note?.Trim();
            target.Note = string.IsNullOrEmpty(note) ? null : note;
        }

        context.Item.ReviewProgress = RoadmapReviewState.WriteChecklist(checklist);
        context.Item.RowVersion += 1;
        _unitOfWork.Repository<ModerationItem>().Update(context.Item);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _auditLogWriter.LogAsync(
            context.Actor.Id, "RoadmapReviewChecklistSaved", nameof(ModerationItem), context.Item.Id,
            beforeState: beforeState,
            afterState: checklist.Select(entry => new { entry.Code, entry.Passed }).ToList(),
            cancellationToken: cancellationToken);
        return checklist;
    }

    private async Task<ReviewContext> LoadClaimedContextAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken)
    {
        var actor = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(actorUserId, cancellationToken);
        if (actor == null || actor.IsDeleted || actor.Status != AccountStatus.Active
            || actor.Role is not (UserRole.Moderator or UserRole.Administrator))
        {
            throw new ForbiddenException(
                "Chỉ Moderator hoặc Administrator đang hoạt động mới duyệt được bộ truyện mẫu.");
        }

        var item = await _unitOfWork.Repository<ModerationItem>().GetByIdAsync(itemId, cancellationToken)
                   ?? throw new NotFoundException("Mục kiểm duyệt", itemId);
        if (item.ItemType != ModerationItemType.RoadmapTemplateReview)
        {
            throw new BadRequestException("Mục này không phải mục duyệt bộ truyện mẫu.");
        }

        if (item.Status != ModerationItemStatus.Claimed || item.ClaimedByUserId != actor.Id)
        {
            throw new ForbiddenException("Bạn chỉ được thao tác trên mục đang do chính mình nhận.");
        }

        if (item.ClaimedAt == null
            || item.ClaimedAt.Value.Add(ModerationQueuePolicy.ClaimTimeout) <= DateTime.UtcNow)
        {
            throw new ForbiddenException("Lượt nhận mục đã quá 2 giờ; hãy nhận lại mục từ hàng đợi.");
        }

        var roadmapId = item.ReadingRoadmapId ?? 0;
        var roadmap = await _unitOfWork.Repository<ReadingRoadmap>()
            .GetByIdAsync(roadmapId, cancellationToken);
        if (roadmap == null || roadmap.IsDeleted)
        {
            throw new NotFoundException("Bộ truyện mẫu", roadmapId);
        }

        if (roadmap.Status != RoadmapTemplateStatus.PendingReview)
        {
            throw new ConflictException("Bộ truyện mẫu này không còn ở trạng thái chờ duyệt.");
        }

        var items = (await _unitOfWork.Repository<ReadingRoadmapItem>().FindAsync(
                candidate => candidate.ReadingRoadmapId == roadmapId,
                cancellationToken: cancellationToken))
            .OrderBy(candidate => candidate.Position).ToList();
        return new ReviewContext(actor, item, roadmap, items);
    }

    private static List<RoadmapReviewBlockerDto> EvaluateApproveBlockers(
        ReviewContext context,
        IReadOnlyList<RoadmapCheck> checks,
        IReadOnlyList<RoadmapChecklistEntryDto> checklist)
    {
        var blockers = new List<RoadmapReviewBlockerDto>();
        if (context.Roadmap.CreatedByUserId == context.Actor.Id)
        {
            blockers.Add(new RoadmapReviewBlockerDto
            {
                Code = RoadmapReviewPolicy.BlockerCodes.OwnRoadmap,
                Message = "Bạn không thể duyệt bộ truyện mẫu do chính mình tạo."
            });
        }

        var notPassed = checklist.Where(entry => !entry.Passed).ToList();
        if (notPassed.Count > 0)
        {
            blockers.Add(new RoadmapReviewBlockerDto
            {
                Code = RoadmapReviewPolicy.BlockerCodes.ChecklistIncomplete,
                Message = "Checklist chưa đạt đủ 4 tiêu chí (còn: "
                          + string.Join("; ", notPassed.Select(entry =>
                              RoadmapReviewPolicy.CriterionLabel(entry.Code)))
                          + "). Nếu bộ chưa đạt, hãy chọn Yêu cầu sửa."
            });
        }

        var ready = checks.Single(check => check.Code == RoadmapCheckCodes.StoriesReady);
        if (!ready.Passed)
        {
            blockers.Add(new RoadmapReviewBlockerDto
            {
                Code = RoadmapReviewPolicy.BlockerCodes.StoriesNotReady,
                Message = ready.Message + " Hãy chọn Yêu cầu sửa."
            });
        }

        var combo = checks.Single(check => check.Code == RoadmapCheckCodes.ComboUnique);
        if (!combo.Passed)
        {
            blockers.Add(new RoadmapReviewBlockerDto
            {
                Code = RoadmapReviewPolicy.BlockerCodes.ComboTaken,
                Message = combo.Message + " Hãy chọn Yêu cầu sửa.",
                RelatedRoadmapId = combo.RelatedRoadmapId
            });
        }

        return blockers;
    }

    private async Task NotifySubmitterAsync(
        ReviewContext context, string decisionKey, string? reason, CancellationToken cancellationToken)
    {
        var submitterId = context.Item.CreatedByUserId;
        if (!submitterId.HasValue)
        {
            return;
        }

        var submitter = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(submitterId.Value, cancellationToken);
        if (submitter == null || submitter.IsDeleted || submitter.Status != AccountStatus.Active)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            itemId = context.Item.Id,
            itemType = ModerationItemType.RoadmapTemplateReview.ToString(),
            roadmapId = context.Roadmap.Id,
            roadmapName = context.Roadmap.Name,
            decision = decisionKey,
            reason
        });
        await _notificationService.CreateAsync(
            submitterId.Value, NotificationType.ContentModerationResult, payload, cancellationToken);
    }

    private static string DisplayName(UserAccount user) =>
        string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName;
}
