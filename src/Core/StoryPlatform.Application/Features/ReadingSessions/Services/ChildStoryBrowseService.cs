using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.ChildRoadmaps.Services;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Application.Features.RoadmapTemplates;
using StoryPlatform.Application.Features.Stories.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

public class ChildStoryBrowseService : IChildStoryBrowseService
{
    private const string MineSource = "mine";
    private const string LibrarySource = "library";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildRoadmapEligibility _eligibility;
    private readonly IStoryCoverResolver? _covers;

    public ChildStoryBrowseService(IUnitOfWork unitOfWork, IChildRoadmapEligibility eligibility, IStoryCoverResolver? covers = null)
    {
        _unitOfWork = unitOfWork;
        _eligibility = eligibility;
        _covers = covers;
    }

    public async Task<ChildStoryPageDto> SearchAsync(
        int childProfileId, ChildStoryQueryDto query, CancellationToken cancellationToken = default)
    {
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken);
        if (child == null || child.IsDeleted || child.Status != ChildProfileStatus.Active)
        {
            throw new ForbiddenException(ChildReadingPolicy.ProfileUnavailableMessage);
        }

        var pageIndex = Math.Max(query.PageIndex, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, ChildReadingPolicy.MaxPageSize);

        var age = RoadmapAge.ToText(child.AgeBand);
        var source = query.Source?.Trim().ToLowerInvariant();
        var search = query.Search?.Trim().ToLowerInvariant();
        var wantMine = source != LibrarySource;
        var wantLibrary = source != MineSource;
        var selected = await _unitOfWork.Repository<ChildRoadmapItem>().FindAsync(item => !item.IsDeleted
            && item.ChildProfileId == childProfileId && item.Source == ChildRoadmapItemSource.Manual
            && (item.Status == ChildRoadmapItemStatus.Waiting || item.Status == ChildRoadmapItemStatus.Open
                || item.Status == ChildRoadmapItemStatus.Completed), cancellationToken: cancellationToken);
        // List, không phải mảng: EF Core trên .NET 10 không dịch được int[].Contains (thành ReadOnlySpan) trong biểu thức truy vấn nên trả 500.
        var selectedIds = selected.Select(item => item.StoryId).Distinct().ToList();

        var (rows, total) = await _unitOfWork.Repository<Story>().GetPagedAsync(
            pageIndex,
            pageSize,
            story => !story.IsDeleted
                     && story.Status == StoryStatus.Ready
                     && story.ArchivedAt == null
                     && (story.AgeBand == age || selectedIds.Contains(story.Id))
                     && ((wantMine && ((story.Scope == StoryScope.Parent && story.ChildProfileId == childProfileId) || selectedIds.Contains(story.Id)))
                         || (wantLibrary && story.Scope == StoryScope.Creator && story.IsPublished && story.AgeBand == age))
                     && (search == null || search == string.Empty
                         || (story.Title != null && story.Title.ToLower().Contains(search))),
            orderBy: stories => stories.OrderByDescending(story => story.CreatedAt).ThenByDescending(story => story.Id),
            cancellationToken: cancellationToken);

        var cover = _covers is null ? null : await _covers.ResolveAsync(rows.Select(row => row.CoverImageUrl), cancellationToken);
        var items = new List<ChildStoryItemDto>();
        foreach (var story in rows)
        {
            if (await _eligibility.EvaluateAsync(child, story, cancellationToken) != null)
            {
                continue;
            }

            items.Add(new ChildStoryItemDto
            {
                StoryId = story.Id,
                Title = story.Title ?? string.Empty,
                CoverImageUrl = cover is null ? story.CoverImageUrl : cover(story.CoverImageUrl),
                Description = story.Description,
                Source = story.Scope == StoryScope.Parent || selectedIds.Contains(story.Id) ? MineSource : LibrarySource
            });
        }

        return new ChildStoryPageDto
        {
            Items = items,
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }
}
