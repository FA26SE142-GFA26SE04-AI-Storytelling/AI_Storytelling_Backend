using System.Linq.Expressions;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ContentReports.DTOs;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.RoadmapTemplates;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentReports.Services;

public class PublishedContentReviewService : IPublishedContentReviewService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;

    public PublishedContentReviewService(
        IUnitOfWork unitOfWork,
        IAuditLogWriter auditLogWriter)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<PagedResult<PublishedContentItemDto>> ListAsync(
        int actorUserId, PublishedContentQueryDto query, CancellationToken cancellationToken = default)
    {
        await RequireStaffAsync(actorUserId, cancellationToken);
        if (!Enum.IsDefined(query.Source))
        {
            throw new BadRequestException("Source phải là PublicLibrary hoặc RoadmapTemplate.");
        }

        var ageBand = ParseAgeBand(query.AgeBand);
        if (query.PublishedFrom.HasValue && query.PublishedTo.HasValue
            && query.PublishedFrom > query.PublishedTo)
        {
            throw new BadRequestException("PublishedFrom không được sau PublishedTo.");
        }

        var pageIndex = Math.Max(query.PageIndex, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        return query.Source == PublishedContentSource.PublicLibrary
            ? await ListStoriesAsync(query, ageBand, pageIndex, pageSize, cancellationToken)
            : await ListRoadmapsAsync(query, ageBand, pageIndex, pageSize, cancellationToken);
    }

    public async Task<PublishedStoryContentDto> GetStoryContentAsync(
        int actorUserId, int storyId, CancellationToken cancellationToken = default)
    {
        var actor = await RequireStaffAsync(actorUserId, cancellationToken);
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken);
        if (story == null || story.IsDeleted)
        {
            throw new NotFoundException("Truyện", storyId);
        }

        // Không cần bước "Xem để rà soát": mọi lần mở đều được ghi audit PublishedContentOpened bên dưới.
        if (story.Scope != StoryScope.Creator || !story.IsPublished
            || story.Status == StoryStatus.Rejected || story.ArchivedAt != null)
        {
            throw new BadRequestException("Chỉ xem được truyện Public Library đang công khai.");
        }

        string? Mask(string? text) => ModeratorTextMasker.Mask(text, null);
        var content = new PublishedStoryContentDto
        {
            Story = ReportedContentBuilder.BuildStory(
                story,
                await ReportedContentBuilder.LoadLabelsAsync(_unitOfWork, story.Id, cancellationToken),
                Mask)
        };

        var version = await ReportedContentBuilder.FindDisplayedVersionAsync(
            _unitOfWork, story, cancellationToken);
        if (version != null)
        {
            var snapshot = await PublicVersionSnapshot.LoadAsync(_unitOfWork, version, cancellationToken);
            content.Version = ReportedContentBuilder.BuildVersion(version, Mask);
            content.Scenes = ReportedContentBuilder.BuildScenes(snapshot, Mask);
            content.Vocabulary = ReportedContentBuilder.BuildVocabulary(snapshot, Mask);
            content.Quizzes = ReportedContentBuilder.BuildQuizzes(snapshot, Mask);
            content.Listening = ReportedContentBuilder.BuildListening(snapshot, Mask);
        }

        await _auditLogWriter.LogAsync(
            actor.Id, "PublishedContentOpened", nameof(Story), story.Id,
            beforeState: null,
            afterState: new { storyId = story.Id, storyVersionId = version?.Id },
            cancellationToken: cancellationToken);
        return content;
    }

    public async Task<PublishedRoadmapDto> GetRoadmapAsync(
        int actorUserId, int roadmapId, CancellationToken cancellationToken = default)
    {
        var actor = await RequireStaffAsync(actorUserId, cancellationToken);
        var roadmap = await _unitOfWork.Repository<ReadingRoadmap>().GetByIdAsync(roadmapId, cancellationToken);
        if (roadmap == null || roadmap.IsDeleted)
        {
            throw new NotFoundException("Bộ truyện mẫu", roadmapId);
        }

        // Không cần bước "Xem để rà soát": mọi lần mở đều được ghi audit PublishedRoadmapOpened bên dưới.
        if (roadmap.Status != RoadmapTemplateStatus.Published || roadmap.ArchivedAt != null)
        {
            throw new BadRequestException("Chỉ xem được bộ truyện mẫu đang công khai.");
        }

        string? Mask(string? text) => ModeratorTextMasker.Mask(text, null);
        var items = (await _unitOfWork.Repository<ReadingRoadmapItem>().FindAsync(
                item => item.ReadingRoadmapId == roadmap.Id, cancellationToken: cancellationToken))
            .OrderBy(item => item.Position).ToList();
        var storyIds = items.Select(item => item.StoryId).ToList();
        var stories = await _unitOfWork.Repository<Story>().FindAsync(
            story => storyIds.Contains(story.Id), cancellationToken: cancellationToken);
        var categories = await LoadCategoriesAsync([roadmap.MainCategoryId], cancellationToken);

        var dto = new PublishedRoadmapDto
        {
            RoadmapId = roadmap.Id,
            Name = Mask(roadmap.Name) ?? string.Empty,
            Description = Mask(roadmap.Description),
            AgeBand = AgeText(roadmap.AgeBand),
            MainCategory = categories.GetValueOrDefault(roadmap.MainCategoryId),
            ReadingLevelMin = roadmap.ReadingLevelMin,
            ReadingLevelMax = roadmap.ReadingLevelMax,
            DefaultDaysPerStory = roadmap.DefaultDaysPerStory,
            PublishedAt = roadmap.PublishedAt,
            NeedsUpdate = items.Any(item =>
                RoadmapNeedsUpdate.IsRetired(
                    stories.FirstOrDefault(candidate => candidate.Id == item.StoryId))),
            Stories = items.Select(item =>
            {
                var story = stories.FirstOrDefault(candidate => candidate.Id == item.StoryId);
                return new PublishedRoadmapStoryDto
                {
                    StoryId = item.StoryId,
                    Position = item.Position,
                    Days = item.Days,
                    Title = Mask(story?.Title),
                    AgeBand = story?.AgeBand ?? string.Empty,
                    IsArchived = story != null
                                 && story.ArchivedAt != null
                };
            }).ToList()
        };

        await _auditLogWriter.LogAsync(
            actor.Id, "PublishedRoadmapOpened", nameof(ReadingRoadmap), roadmap.Id,
            beforeState: null,
            afterState: new { roadmapId = roadmap.Id, storyCount = items.Count },
            cancellationToken: cancellationToken);
        return dto;
    }

    private async Task<PagedResult<PublishedContentItemDto>> ListStoriesAsync(
        PublishedContentQueryDto query, AgeBand? ageBand, int pageIndex, int pageSize,
        CancellationToken cancellationToken)
    {
        var filterByCategory = query.ContentCategoryId.HasValue;
        var categoryStoryIds = new List<int>();
        if (filterByCategory)
        {
            var categoryId = query.ContentCategoryId!.Value;
            var categoryLinks = await _unitOfWork.Repository<StoryCategory>().FindAsync(
                link => link.ContentCategoryId == categoryId, cancellationToken: cancellationToken);
            categoryStoryIds = categoryLinks.Select(link => link.StoryId).Distinct().ToList();
        }

        var filterByAge = ageBand.HasValue;
        var ageText = ageBand.HasValue ? AgeText(ageBand.Value) : string.Empty;
        var filterFrom = query.PublishedFrom.HasValue;
        var from = query.PublishedFrom ?? DateTime.MinValue;
        var filterTo = query.PublishedTo.HasValue;
        var to = query.PublishedTo ?? DateTime.MaxValue;
        Expression<Func<Story, bool>> filter = story =>
            !story.IsDeleted && story.Scope == StoryScope.Creator && story.IsPublished
            && story.Status != StoryStatus.Rejected && story.ArchivedAt == null
            && (!filterByAge || story.AgeBand == ageText)
            && (!filterByCategory || categoryStoryIds.Contains(story.Id))
            && (!filterFrom || story.PublishedAt >= from)
            && (!filterTo || story.PublishedAt <= to);

        var (stories, total) = await _unitOfWork.Repository<Story>().GetPagedAsync(
            pageIndex, pageSize, filter,
            orderBy: source => source.OrderByDescending(story => story.PublishedAt)
                .ThenByDescending(story => story.Id),
            cancellationToken: cancellationToken);

        var storyIds = stories.Select(story => story.Id).ToList();
        var links = await _unitOfWork.Repository<StoryCategory>().FindAsync(
            link => storyIds.Contains(link.StoryId), cancellationToken: cancellationToken);
        var categories = await LoadCategoriesAsync(
            links.Select(link => link.ContentCategoryId).Distinct().ToList(), cancellationToken);
        var items = stories.Select(story => new PublishedContentItemDto
        {
            Source = PublishedContentSource.PublicLibrary.ToString(),
            Id = story.Id,
            Title = story.Title ?? string.Empty,
            AgeBand = story.AgeBand,
            PublishedAt = story.PublishedAt,
            Labels = links.Where(link => link.StoryId == story.Id)
                .Select(link => categories.GetValueOrDefault(link.ContentCategoryId))
                .Where(label => label != null).Select(label => label!)
                .OrderBy(label => label.Code).ToList()
        }).ToList();
        return new PagedResult<PublishedContentItemDto>(items, total, pageIndex, pageSize);
    }

    private async Task<PagedResult<PublishedContentItemDto>> ListRoadmapsAsync(
        PublishedContentQueryDto query, AgeBand? ageBand, int pageIndex, int pageSize,
        CancellationToken cancellationToken)
    {
        var filterByAge = ageBand.HasValue;
        var ageValue = ageBand ?? AgeBand.Age_6_8;
        var filterByCategory = query.ContentCategoryId.HasValue;
        var categoryId = query.ContentCategoryId ?? 0;
        var filterFrom = query.PublishedFrom.HasValue;
        var from = query.PublishedFrom ?? DateTime.MinValue;
        var filterTo = query.PublishedTo.HasValue;
        var to = query.PublishedTo ?? DateTime.MaxValue;
        Expression<Func<ReadingRoadmap, bool>> filter = roadmap =>
            !roadmap.IsDeleted && roadmap.Status == RoadmapTemplateStatus.Published && roadmap.ArchivedAt == null
            && (!filterByAge || roadmap.AgeBand == ageValue)
            && (!filterByCategory || roadmap.MainCategoryId == categoryId)
            && (!filterFrom || roadmap.PublishedAt >= from)
            && (!filterTo || roadmap.PublishedAt <= to);

        var (roadmaps, total) = await _unitOfWork.Repository<ReadingRoadmap>().GetPagedAsync(
            pageIndex, pageSize, filter,
            orderBy: source => source.OrderByDescending(roadmap => roadmap.PublishedAt)
                .ThenByDescending(roadmap => roadmap.Id),
            cancellationToken: cancellationToken);
        var roadmapIds = roadmaps.Select(roadmap => roadmap.Id).ToList();
        var roadmapItems = await _unitOfWork.Repository<ReadingRoadmapItem>().FindAsync(
            item => roadmapIds.Contains(item.ReadingRoadmapId), cancellationToken: cancellationToken);
        var categories = await LoadCategoriesAsync(
            roadmaps.Select(roadmap => roadmap.MainCategoryId).Distinct().ToList(), cancellationToken);
        var retired = await RoadmapNeedsUpdate.RetiredStoriesByRoadmapAsync(
            _unitOfWork, roadmapItems, cancellationToken);
        var items = roadmaps.Select(roadmap => new PublishedContentItemDto
        {
            Source = PublishedContentSource.RoadmapTemplate.ToString(),
            Id = roadmap.Id,
            Title = roadmap.Name,
            AgeBand = AgeText(roadmap.AgeBand),
            PublishedAt = roadmap.PublishedAt,
            StoryCount = roadmapItems.Count(item => item.ReadingRoadmapId == roadmap.Id),
            NeedsUpdate = retired.ContainsKey(roadmap.Id),
            Labels = categories.TryGetValue(roadmap.MainCategoryId, out var label)
                ? [label]
                : []
        }).ToList();
        return new PagedResult<PublishedContentItemDto>(items, total, pageIndex, pageSize);
    }

    private async Task<Dictionary<int, ReportedLabelDto>> LoadCategoriesAsync(
        List<int> categoryIds, CancellationToken cancellationToken)
    {
        var categories = await _unitOfWork.Repository<ContentCategory>().FindAsync(
            category => categoryIds.Contains(category.Id), cancellationToken: cancellationToken);
        return categories.ToDictionary(
            category => category.Id,
            category => new ReportedLabelDto
            {
                Code = category.Code,
                DisplayName = category.DisplayName
            });
    }

    private async Task<UserAccount> RequireStaffAsync(int actorUserId, CancellationToken cancellationToken)
    {
        var actor = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(actorUserId, cancellationToken);
        if (actor == null || actor.IsDeleted || actor.Status != AccountStatus.Active
            || actor.Role is not (UserRole.Moderator or UserRole.Administrator))
        {
            throw new ForbiddenException(
                "Chỉ Moderator hoặc Administrator đang hoạt động mới xem được nội dung đã công khai.");
        }

        return actor;
    }

    private static AgeBand? ParseAgeBand(string? ageBand)
    {
        var trimmed = ageBand?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed switch
        {
            "6-8" => AgeBand.Age_6_8,
            "9-10" => AgeBand.Age_9_10,
            _ => throw new BadRequestException("Độ tuổi phải là 6-8 hoặc 9-10.")
        };
    }

    private static string AgeText(AgeBand ageBand) => ageBand == AgeBand.Age_6_8 ? "6-8" : "9-10";
}
