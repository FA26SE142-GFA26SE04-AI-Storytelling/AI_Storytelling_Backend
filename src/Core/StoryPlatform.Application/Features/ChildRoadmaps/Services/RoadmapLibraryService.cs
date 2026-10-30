using System.Linq.Expressions;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.RoadmapTemplates;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

public partial class RoadmapLibraryService : IRoadmapLibraryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildProfileAccessGuard _guard;
    private readonly IChildRoadmapEligibility _eligibility;
    private readonly IChildRoadmapInserter _inserter;
    private readonly IChildRoadmapAdvancer _advancer;
    private readonly IChildRoadmapNotifier _notifier;
    private readonly IAuditLogWriter _auditLogWriter;

    public RoadmapLibraryService(
        IUnitOfWork unitOfWork,
        IChildProfileAccessGuard guard,
        IChildRoadmapEligibility eligibility,
        IChildRoadmapInserter inserter,
        IChildRoadmapAdvancer advancer,
        IChildRoadmapNotifier notifier,
        IAuditLogWriter auditLogWriter)
    {
        _unitOfWork = unitOfWork;
        _guard = guard;
        _eligibility = eligibility;
        _inserter = inserter;
        _advancer = advancer;
        _notifier = notifier;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<PagedResult<RoadmapLibraryItemDto>> ListAsync(
        int actorUserId, RoadmapLibraryQueryDto query, CancellationToken cancellationToken = default)
    {
        await RequireParentAsync(actorUserId, cancellationToken);

        var ageBand = RoadmapAge.Parse(query.AgeBand);
        var pageIndex = Math.Max(query.PageIndex, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var filterByAge = ageBand.HasValue;
        var ageValue = ageBand ?? AgeBand.Age_6_8;
        var filterByLevel = query.ReadingLevel.HasValue;
        var level = query.ReadingLevel ?? 0;
        var filterByCategory = query.ContentCategoryId.HasValue;
        var categoryId = query.ContentCategoryId ?? 0;

        Expression<Func<ReadingRoadmap, bool>> filter = roadmap =>
            !roadmap.IsDeleted
            && roadmap.Status == RoadmapTemplateStatus.Published
            && roadmap.ArchivedAt == null
            && (!filterByAge || roadmap.AgeBand == ageValue)
            && (!filterByLevel || (roadmap.ReadingLevelMin <= level && roadmap.ReadingLevelMax >= level))
            && (!filterByCategory || roadmap.MainCategoryId == categoryId);

        var (roadmaps, total) = await _unitOfWork.Repository<ReadingRoadmap>().GetPagedAsync(
            pageIndex, pageSize, filter,
            orderBy: source => source.OrderByDescending(roadmap => roadmap.PublishedAt)
                .ThenByDescending(roadmap => roadmap.Id),
            cancellationToken: cancellationToken);

        var roadmapIds = roadmaps.Select(roadmap => roadmap.Id).ToList();
        var items = await _unitOfWork.Repository<ReadingRoadmapItem>().FindAsync(
            item => roadmapIds.Contains(item.ReadingRoadmapId), cancellationToken: cancellationToken);
        var retired = await RoadmapNeedsUpdate.RetiredStoriesByRoadmapAsync(_unitOfWork, items, cancellationToken);
        var categories = await LoadCategoriesAsync(
            roadmaps.Select(roadmap => roadmap.MainCategoryId).Distinct().ToList(), cancellationToken);

        var rows = roadmaps.Select(roadmap => ToItem(
            roadmap,
            items.Count(item => item.ReadingRoadmapId == roadmap.Id),
            retired.ContainsKey(roadmap.Id),
            categories)).ToList();
        return new PagedResult<RoadmapLibraryItemDto>(rows, total, pageIndex, pageSize);
    }

    public async Task<RoadmapLibraryDetailDto> GetAsync(
        int actorUserId, int roadmapId, CancellationToken cancellationToken = default)
    {
        await RequireParentAsync(actorUserId, cancellationToken);
        var roadmap = await LoadPublishedRoadmapAsync(roadmapId, cancellationToken);
        var items = (await _unitOfWork.Repository<ReadingRoadmapItem>().FindAsync(
                item => item.ReadingRoadmapId == roadmapId, cancellationToken: cancellationToken))
            .OrderBy(item => item.Position).ToList();
        var storyIds = items.Select(item => item.StoryId).Distinct().ToList();
        var stories = await _unitOfWork.Repository<Story>().FindAsync(
            story => storyIds.Contains(story.Id), cancellationToken: cancellationToken);
        var byId = stories.ToDictionary(story => story.Id);
        var categories = await LoadCategoriesAsync(new List<int> { roadmap.MainCategoryId }, cancellationToken);

        var stepDtos = items.Select(item =>
        {
            byId.TryGetValue(item.StoryId, out var story);
            return new RoadmapLibraryStoryDto
            {
                StoryId = item.StoryId,
                Position = item.Position,
                Days = item.Days,
                Title = story?.Title,
                AgeBand = story?.AgeBand ?? string.Empty,
                ReadingLevel = story?.ReadingLevel,
                IsAvailable = !RoadmapNeedsUpdate.IsRetired(story)
            };
        }).ToList();

        var detail = new RoadmapLibraryDetailDto { Stories = stepDtos };
        Fill(detail, roadmap, items.Count, stepDtos.Any(step => !step.IsAvailable), categories);
        return detail;
    }

    private async Task<ReadingRoadmap> LoadPublishedRoadmapAsync(
        int roadmapId, CancellationToken cancellationToken)
    {
        var roadmap = await _unitOfWork.Repository<ReadingRoadmap>().GetByIdAsync(roadmapId, cancellationToken);
        if (roadmap == null
            || roadmap.IsDeleted
            || roadmap.Status != RoadmapTemplateStatus.Published
            || roadmap.ArchivedAt != null)
        {
            throw new NotFoundException("Bộ truyện mẫu", roadmapId);
        }

        return roadmap;
    }

    private async Task<UserAccount> RequireParentAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken);
        if (user == null || user.IsDeleted || user.Status != AccountStatus.Active || user.Role != UserRole.User)
        {
            throw new ForbiddenException("Chỉ phụ huynh đang hoạt động mới dùng được Kho bộ truyện mẫu.");
        }

        return user;
    }

    private async Task<Dictionary<int, RoadmapCategoryDto>> LoadCategoriesAsync(
        List<int> categoryIds, CancellationToken cancellationToken)
    {
        var categories = await _unitOfWork.Repository<ContentCategory>().FindAsync(
            category => categoryIds.Contains(category.Id), cancellationToken: cancellationToken);
        return categories.ToDictionary(
            category => category.Id,
            category => new RoadmapCategoryDto
            {
                Id = category.Id,
                Code = category.Code,
                DisplayName = category.DisplayName
            });
    }

    private static RoadmapLibraryItemDto ToItem(
        ReadingRoadmap roadmap,
        int storyCount,
        bool needsUpdate,
        Dictionary<int, RoadmapCategoryDto> categories)
    {
        var dto = new RoadmapLibraryItemDto();
        Fill(dto, roadmap, storyCount, needsUpdate, categories);
        return dto;
    }

    private static void Fill(
        RoadmapLibraryItemDto dto,
        ReadingRoadmap roadmap,
        int storyCount,
        bool needsUpdate,
        Dictionary<int, RoadmapCategoryDto> categories)
    {
        dto.Id = roadmap.Id;
        dto.Name = roadmap.Name;
        dto.Description = roadmap.Description;
        dto.AgeBand = RoadmapAge.ToText(roadmap.AgeBand);
        dto.MainCategory = categories.GetValueOrDefault(roadmap.MainCategoryId);
        dto.ReadingLevelMin = roadmap.ReadingLevelMin;
        dto.ReadingLevelMax = roadmap.ReadingLevelMax;
        dto.DefaultDaysPerStory = roadmap.DefaultDaysPerStory;
        dto.StoryCount = storyCount;
        dto.NeedsUpdate = needsUpdate;
        dto.PublishedAt = roadmap.PublishedAt;
    }
}
