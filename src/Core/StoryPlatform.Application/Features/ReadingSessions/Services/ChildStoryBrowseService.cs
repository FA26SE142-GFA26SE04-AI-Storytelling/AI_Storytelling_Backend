using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Application.Features.RoadmapTemplates;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

public class ChildStoryBrowseService : IChildStoryBrowseService
{
    private const string MineSource = "mine";
    private const string LibrarySource = "library";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildRoadmapEligibility _eligibility;

    public ChildStoryBrowseService(IUnitOfWork unitOfWork, IChildRoadmapEligibility eligibility)
    {
        _unitOfWork = unitOfWork;
        _eligibility = eligibility;
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

        var hold = await HoldModeLookup.FindOpenAsync(_unitOfWork, childProfileId, cancellationToken);
        if (hold != null)
        {
            return new ChildStoryPageDto
            {
                IsLocked = true,
                UnlockStoryId = hold.TriggeringStoryId,
                Message = ChildReadingPolicy.LibraryLockedMessage,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }

        var age = RoadmapAge.ToText(child.AgeBand);
        var source = query.Source?.Trim().ToLowerInvariant();
        var search = query.Search?.Trim().ToLowerInvariant();
        var wantMine = source != LibrarySource;
        var wantLibrary = source != MineSource;

        var (rows, total) = await _unitOfWork.Repository<Story>().GetPagedAsync(
            pageIndex,
            pageSize,
            story => !story.IsDeleted
                     && story.Status == StoryStatus.Ready
                     && story.ArchivedAt == null
                     && story.AgeBand == age
                     && ((wantMine && story.Scope == StoryScope.Parent && story.ChildProfileId == childProfileId)
                         || (wantLibrary && story.Scope == StoryScope.Creator && story.IsPublished))
                     && (search == null || search == string.Empty
                         || (story.Title != null && story.Title.ToLower().Contains(search))),
            orderBy: stories => stories.OrderByDescending(story => story.CreatedAt).ThenByDescending(story => story.Id),
            cancellationToken: cancellationToken);

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
                CoverImageUrl = story.CoverImageUrl,
                Description = story.Description,
                Source = story.Scope == StoryScope.Parent ? MineSource : LibrarySource
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
