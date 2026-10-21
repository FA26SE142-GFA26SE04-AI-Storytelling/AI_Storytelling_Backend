using System.Linq.Expressions;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.RoadmapTemplates.Services;

public partial class RoadmapTemplateService
{
    public async Task<PagedResult<CandidateStoryDto>> ListCandidatesAsync(
        int actorUserId, CandidateQueryDto query, CancellationToken cancellationToken = default)
    {
        await RequireStaffAsync(actorUserId, cancellationToken);
        var ageBand = RoadmapAge.Parse(query.AgeBand);
        var pageIndex = Math.Max(query.PageIndex, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

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
        var ageText = ageBand.HasValue ? RoadmapAge.ToText(ageBand.Value) : string.Empty;
        var filterMin = query.ReadingLevelMin.HasValue;
        var levelMin = query.ReadingLevelMin ?? 0;
        var filterMax = query.ReadingLevelMax.HasValue;
        var levelMax = query.ReadingLevelMax ?? 0;
        Expression<Func<Story, bool>> filter = story =>
            !story.IsDeleted
            && story.Scope == StoryScope.Creator
            && story.IsPublished
            && story.Status == StoryStatus.Ready
            && story.ArchivedAt == null
            && (!filterByAge || story.AgeBand == ageText)
            && (!filterMin || story.ReadingLevel >= levelMin)
            && (!filterMax || story.ReadingLevel <= levelMax)
            && (!filterByCategory || categoryStoryIds.Contains(story.Id));

        var (stories, total) = await _unitOfWork.Repository<Story>().GetPagedAsync(
            pageIndex, pageSize, filter,
            orderBy: source => source.OrderByDescending(story => story.PublishedAt)
                .ThenByDescending(story => story.Id),
            cancellationToken: cancellationToken);

        var storyIds = stories.Select(story => story.Id).ToList();
        var links = await _unitOfWork.Repository<StoryCategory>().FindAsync(
            link => storyIds.Contains(link.StoryId), cancellationToken: cancellationToken);
        var categoryIds = links.Select(link => link.ContentCategoryId).Distinct().ToList();
        var categories = await _unitOfWork.Repository<ContentCategory>().FindAsync(
            category => categoryIds.Contains(category.Id), cancellationToken: cancellationToken);
        var labelById = categories.ToDictionary(
            category => category.Id,
            category => new RoadmapCategoryDto
            {
                Id = category.Id,
                Code = category.Code,
                DisplayName = category.DisplayName
            });

        var items = stories.Select(story => new CandidateStoryDto
        {
            StoryId = story.Id,
            Title = story.Title,
            AgeBand = story.AgeBand,
            ReadingLevel = story.ReadingLevel,
            PublishedAt = story.PublishedAt,
            Labels = links.Where(link => link.StoryId == story.Id)
                .Select(link => labelById.GetValueOrDefault(link.ContentCategoryId))
                .Where(label => label != null).Select(label => label!)
                .OrderBy(label => label.Code).ToList()
        }).ToList();

        return new PagedResult<CandidateStoryDto>(items, total, pageIndex, pageSize);
    }

    public async Task<RoadmapDetailDto> SetItemsAsync(
        int actorUserId,
        int roadmapId,
        SetRoadmapItemsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireModeratorAsync(actorUserId, cancellationToken);
        var roadmap = await LoadOwnedDraftAsync(actor, roadmapId, cancellationToken);
        var inputs = request.Items ?? throw new BadRequestException("Danh sách truyện không hợp lệ.");

        var duplicated = inputs.GroupBy(input => input.StoryId).Where(group => group.Count() > 1)
            .Select(group => group.Key).OrderBy(id => id).ToList();
        if (duplicated.Count > 0)
        {
            throw new BadRequestException($"Mỗi truyện chỉ được chọn một lần: {string.Join(", ", duplicated)}.");
        }

        var badDays = inputs.Where(input => input.Days.HasValue
                                            && (input.Days < RoadmapTemplatePolicy.MinDays
                                                || input.Days > RoadmapTemplatePolicy.MaxDays))
            .Select(input => input.StoryId).ToList();
        if (badDays.Count > 0)
        {
            throw new BadRequestException(
                $"Số ngày của truyện phải từ {RoadmapTemplatePolicy.MinDays} đến "
                + $"{RoadmapTemplatePolicy.MaxDays}: {string.Join(", ", badDays)}.");
        }

        var storyIds = inputs.Select(input => input.StoryId).ToList();
        var stories = await _unitOfWork.Repository<Story>().FindAsync(
            story => storyIds.Contains(story.Id), cancellationToken: cancellationToken);
        var byId = stories.ToDictionary(story => story.Id);
        var ineligible = storyIds
            .Where(id => !byId.TryGetValue(id, out var story) || !RoadmapEligibility.IsEligible(story))
            .OrderBy(id => id).ToList();
        if (ineligible.Count > 0)
        {
            throw new BadRequestException(
                $"Truyện không phải Public Library đang công khai: {string.Join(", ", ineligible)}.");
        }

        var itemRepository = _unitOfWork.Repository<ReadingRoadmapItem>();
        var existing = await itemRepository.FindAsync(
            item => item.ReadingRoadmapId == roadmap.Id, cancellationToken: cancellationToken);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (existing.Count > 0)
            {
                itemRepository.DeleteRange(existing);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            if (inputs.Count > 0)
            {
                await itemRepository.AddRangeAsync(
                    inputs.Select((input, index) => new ReadingRoadmapItem
                    {
                        ReadingRoadmapId = roadmap.Id,
                        StoryId = input.StoryId,
                        Position = index + 1,
                        Days = input.Days
                    }),
                    cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            await _auditLogWriter.LogAsync(
                actor.Id, "RoadmapTemplateItemsSet", nameof(ReadingRoadmap), roadmap.Id,
                beforeState: new
                {
                    storyIds = existing.OrderBy(item => item.Position).Select(item => item.StoryId)
                },
                afterState: new { storyIds },
                cancellationToken: cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return await BuildDetailAsync(roadmap, actor, cancellationToken);
    }
}
