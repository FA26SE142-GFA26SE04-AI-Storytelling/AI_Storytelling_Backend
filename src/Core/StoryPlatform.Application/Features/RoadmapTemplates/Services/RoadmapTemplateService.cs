using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;
using StoryPlatform.Application.Features.RoadmapTemplates.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.RoadmapTemplates.Services;

public partial class RoadmapTemplateService : IRoadmapTemplateService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRoadmapSubmissionChecker _checker;
    private readonly IModerationQueueService _queueService;
    private readonly IAuditLogWriter _auditLogWriter;

    public RoadmapTemplateService(
        IUnitOfWork unitOfWork,
        IRoadmapSubmissionChecker checker,
        IModerationQueueService queueService,
        IAuditLogWriter auditLogWriter)
    {
        _unitOfWork = unitOfWork;
        _checker = checker;
        _queueService = queueService;
        _auditLogWriter = auditLogWriter;
    }

    private sealed record ValidatedInput(
        string Name,
        string? Description,
        AgeBand AgeBand,
        int MainCategoryId,
        int ReadingLevelMin,
        int ReadingLevelMax,
        int DefaultDaysPerStory);

    public async Task<RoadmapDetailDto> CreateAsync(
        int actorUserId, SaveRoadmapRequestDto request, CancellationToken cancellationToken = default)
    {
        var actor = await RequireModeratorAsync(actorUserId, cancellationToken);
        var input = await ValidateAsync(request, cancellationToken);
        var roadmap = new ReadingRoadmap
        {
            Name = input.Name,
            Description = input.Description,
            AgeBand = input.AgeBand,
            MainCategoryId = input.MainCategoryId,
            ReadingLevelMin = input.ReadingLevelMin,
            ReadingLevelMax = input.ReadingLevelMax,
            DefaultDaysPerStory = input.DefaultDaysPerStory,
            Status = RoadmapTemplateStatus.Draft,
            CreatedByUserId = actor.Id
        };
        await _unitOfWork.Repository<ReadingRoadmap>().AddAsync(roadmap, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _auditLogWriter.LogAsync(
            actor.Id, "RoadmapTemplateCreated", nameof(ReadingRoadmap), roadmap.Id,
            beforeState: null,
            afterState: new
            {
                name = roadmap.Name,
                ageBand = roadmap.AgeBand.ToString(),
                mainCategoryId = roadmap.MainCategoryId
            },
            cancellationToken: cancellationToken);
        return await BuildDetailAsync(roadmap, actor, cancellationToken);
    }

    public async Task<RoadmapDetailDto> UpdateAsync(
        int actorUserId,
        int roadmapId,
        SaveRoadmapRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireModeratorAsync(actorUserId, cancellationToken);
        var roadmap = await LoadOwnedDraftAsync(actor, roadmapId, cancellationToken);
        var input = await ValidateAsync(request, cancellationToken);
        var before = new
        {
            name = roadmap.Name,
            ageBand = roadmap.AgeBand.ToString(),
            mainCategoryId = roadmap.MainCategoryId,
            readingLevelMin = roadmap.ReadingLevelMin,
            readingLevelMax = roadmap.ReadingLevelMax
        };

        roadmap.Name = input.Name;
        roadmap.Description = input.Description;
        roadmap.AgeBand = input.AgeBand;
        roadmap.MainCategoryId = input.MainCategoryId;
        roadmap.ReadingLevelMin = input.ReadingLevelMin;
        roadmap.ReadingLevelMax = input.ReadingLevelMax;
        roadmap.DefaultDaysPerStory = input.DefaultDaysPerStory;
        _unitOfWork.Repository<ReadingRoadmap>().Update(roadmap);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _auditLogWriter.LogAsync(
            actor.Id, "RoadmapTemplateUpdated", nameof(ReadingRoadmap), roadmap.Id,
            beforeState: before,
            afterState: new
            {
                name = roadmap.Name,
                ageBand = roadmap.AgeBand.ToString(),
                mainCategoryId = roadmap.MainCategoryId,
                readingLevelMin = roadmap.ReadingLevelMin,
                readingLevelMax = roadmap.ReadingLevelMax
            },
            cancellationToken: cancellationToken);
        return await BuildDetailAsync(roadmap, actor, cancellationToken);
    }

    public async Task<RoadmapDetailDto> GetAsync(
        int actorUserId, int roadmapId, CancellationToken cancellationToken = default)
    {
        var actor = await RequireStaffAsync(actorUserId, cancellationToken);
        var roadmap = await _unitOfWork.Repository<ReadingRoadmap>().GetByIdAsync(roadmapId, cancellationToken);
        if (roadmap == null || roadmap.IsDeleted)
        {
            throw new NotFoundException("Bộ truyện mẫu", roadmapId);
        }

        return await BuildDetailAsync(roadmap, actor, cancellationToken);
    }

    private async Task<ReadingRoadmap> LoadOwnedDraftAsync(
        UserAccount actor, int roadmapId, CancellationToken cancellationToken)
    {
        var roadmap = await _unitOfWork.Repository<ReadingRoadmap>().GetByIdAsync(roadmapId, cancellationToken);
        if (roadmap == null || roadmap.IsDeleted)
        {
            throw new NotFoundException("Bộ truyện mẫu", roadmapId);
        }

        if (roadmap.CreatedByUserId != actor.Id)
        {
            throw new ForbiddenException("Chỉ người tạo mới sửa được bộ này.");
        }

        if (roadmap.Status != RoadmapTemplateStatus.Draft)
        {
            throw new ConflictException("Chỉ sửa được bộ ở trạng thái Nháp.");
        }

        return roadmap;
    }

    private async Task<RoadmapDetailDto> BuildDetailAsync(
        ReadingRoadmap roadmap, UserAccount actor, CancellationToken cancellationToken)
    {
        var roadmapId = roadmap.Id;
        var items = (await _unitOfWork.Repository<ReadingRoadmapItem>().FindAsync(
                item => item.ReadingRoadmapId == roadmapId, cancellationToken: cancellationToken))
            .OrderBy(item => item.Position).ToList();
        var checks = await _checker.EvaluateAsync(roadmap, items, cancellationToken);
        var category = await _unitOfWork.Repository<ContentCategory>()
            .GetByIdAsync(roadmap.MainCategoryId, cancellationToken);
        var creator = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(roadmap.CreatedByUserId, cancellationToken);
        var retired = roadmap.Status == RoadmapTemplateStatus.Published
            ? await RoadmapNeedsUpdate.RetiredStoriesByRoadmapAsync(_unitOfWork, items, cancellationToken)
            : new Dictionary<int, List<int>>();

        return new RoadmapDetailDto
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
            Status = roadmap.Status.ToString(),
            CreatedByUserId = roadmap.CreatedByUserId,
            CreatedByName = creator == null ? null : DisplayName(creator),
            IsMine = roadmap.CreatedByUserId == actor.Id,
            SubmittedAt = roadmap.SubmittedAt,
            PublishedAt = roadmap.PublishedAt,
            ClonedFromId = roadmap.ClonedFromId,
            Stories = await RoadmapStoryLoader.LoadAsync(_unitOfWork, items, cancellationToken),
            Checks = checks.Select(check => new RoadmapCheckDto
            {
                Code = check.Code,
                Passed = check.Passed,
                Message = check.Message,
                RelatedRoadmapId = check.RelatedRoadmapId
            }).ToList(),
            CanSubmit = roadmap.Status == RoadmapTemplateStatus.Draft && checks.All(check => check.Passed),
            LastReview = await LoadLastReviewAsync(roadmap, actor, cancellationToken),
            NeedsUpdate = retired.ContainsKey(roadmap.Id),
            RetiredStoryIds = retired.TryGetValue(roadmap.Id, out var retiredIds)
                ? retiredIds
                : new List<int>()
        };
    }

    private async Task<RoadmapLastReviewDto?> LoadLastReviewAsync(
        ReadingRoadmap roadmap, UserAccount actor, CancellationToken cancellationToken)
    {
        if (roadmap.Status != RoadmapTemplateStatus.Draft || roadmap.CreatedByUserId != actor.Id)
        {
            return null;
        }

        var roadmapId = roadmap.Id;
        var reviewItems = await _unitOfWork.Repository<ModerationItem>().FindAsync(
            item => item.ItemType == ModerationItemType.RoadmapTemplateReview
                    && item.ReadingRoadmapId == roadmapId,
            cancellationToken: cancellationToken);
        var latest = reviewItems.OrderByDescending(item => item.Id).FirstOrDefault();
        if (latest == null
            || latest.Status is not (ModerationItemStatus.ChangesRequested or ModerationItemStatus.Rejected))
        {
            return null;
        }

        var feedback = RoadmapReviewState.ReadFeedback(latest.Feedback);
        if (feedback == null)
        {
            return null;
        }

        var reviewer = latest.DecidedByUserId.HasValue
            ? await _unitOfWork.Repository<UserAccount>()
                .GetByIdAsync(latest.DecidedByUserId.Value, cancellationToken)
            : null;
        return new RoadmapLastReviewDto
        {
            Decision = feedback.Decision,
            ReviewerName = reviewer == null ? null : DisplayName(reviewer),
            GeneralNote = feedback.GeneralNote,
            Reason = feedback.Reason,
            Parts = feedback.Parts,
            DecidedAt = latest.DecidedAt
        };
    }

    private async Task<ValidatedInput> ValidateAsync(
        SaveRoadmapRequestDto request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > RoadmapTemplatePolicy.MaxNameLength)
        {
            throw new BadRequestException(
                $"Tên bộ bắt buộc và tối đa {RoadmapTemplatePolicy.MaxNameLength} ký tự.");
        }

        var description = request.Description?.Trim();
        if (string.IsNullOrEmpty(description))
        {
            description = null;
        }
        else if (description.Length > RoadmapTemplatePolicy.MaxDescriptionLength)
        {
            throw new BadRequestException($"Mô tả tối đa {RoadmapTemplatePolicy.MaxDescriptionLength} ký tự.");
        }

        var ageBand = RoadmapAge.Parse(request.AgeBand)
                      ?? throw new BadRequestException("Phải chọn độ tuổi mục tiêu (6-8 hoặc 9-10).");
        var category = await _unitOfWork.Repository<ContentCategory>()
            .GetByIdAsync(request.MainCategoryId, cancellationToken);
        if (category == null || category.IsDeleted || !category.IsActive)
        {
            throw new BadRequestException("Chủ đề chính không tồn tại hoặc không còn hoạt động.");
        }

        if (request.ReadingLevelMin < RoadmapTemplatePolicy.MinReadingLevel
            || request.ReadingLevelMax > RoadmapTemplatePolicy.MaxReadingLevel
            || request.ReadingLevelMin > request.ReadingLevelMax)
        {
            throw new BadRequestException(
                $"Khoảng reading level phải thỏa {RoadmapTemplatePolicy.MinReadingLevel} ≤ nhỏ nhất ≤ lớn nhất ≤ "
                + $"{RoadmapTemplatePolicy.MaxReadingLevel}.");
        }

        if (request.DefaultDaysPerStory < RoadmapTemplatePolicy.MinDays
            || request.DefaultDaysPerStory > RoadmapTemplatePolicy.MaxDays)
        {
            throw new BadRequestException(
                $"Số ngày mặc định mỗi truyện từ {RoadmapTemplatePolicy.MinDays} đến "
                + $"{RoadmapTemplatePolicy.MaxDays}.");
        }

        return new ValidatedInput(
            name, description, ageBand, category.Id, request.ReadingLevelMin,
            request.ReadingLevelMax, request.DefaultDaysPerStory);
    }

    private async Task<UserAccount> RequireModeratorAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken);
        if (user == null || user.IsDeleted || user.Status != AccountStatus.Active || user.Role != UserRole.Moderator)
        {
            throw new ForbiddenException("Chỉ Moderator đang hoạt động mới thao tác được với bộ truyện mẫu.");
        }

        return user;
    }

    private async Task<UserAccount> RequireStaffAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken);
        if (user == null || user.IsDeleted || user.Status != AccountStatus.Active
            || user.Role is not (UserRole.Moderator or UserRole.Administrator))
        {
            throw new ForbiddenException("Chỉ Moderator hoặc Administrator đang hoạt động mới xem được bộ truyện mẫu.");
        }

        return user;
    }

    private static string DisplayName(UserAccount user) =>
        string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName;
}
