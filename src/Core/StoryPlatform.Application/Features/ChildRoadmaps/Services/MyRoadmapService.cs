using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.RoadmapTemplates;
using StoryPlatform.Application.Features.Stories.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

public class MyRoadmapService : IMyRoadmapService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStoryCoverResolver? _covers;

    public MyRoadmapService(IUnitOfWork unitOfWork, IStoryCoverResolver? covers = null)
    {
        _unitOfWork = unitOfWork;
        _covers = covers;
    }

    public async Task<MyRoadmapDto> GetAsync(
        int childProfileId, CancellationToken cancellationToken = default)
    {
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(
            childProfileId, cancellationToken);
        if (child == null || child.IsDeleted || child.Status != ChildProfileStatus.Active)
        {
            throw new ForbiddenException("Hồ sơ này hiện không dùng được lộ trình.");
        }

        var items = await _unitOfWork.Repository<ChildRoadmapItem>().FindAsync(
            item => item.ChildProfileId == childProfileId
                    && (item.Status == ChildRoadmapItemStatus.Open
                        || item.Status == ChildRoadmapItemStatus.Waiting
                        || item.Status == ChildRoadmapItemStatus.Completed),
            cancellationToken: cancellationToken);

        var storyIds = items.Select(item => item.StoryId).Distinct().ToList();
        var stories = await _unitOfWork.Repository<Story>().FindAsync(
            story => storyIds.Contains(story.Id), cancellationToken: cancellationToken);
        var byId = stories.ToDictionary(story => story.Id);
        var resolve = _covers is null ? null : await _covers.ResolveAsync(stories.Select(story => story.CoverImageUrl), cancellationToken);
        string? CoverOf(int storyId) => resolve is null ? byId[storyId].CoverImageUrl : resolve(byId[storyId].CoverImageUrl);

        var visible = items.Where(item =>
            byId.TryGetValue(item.StoryId, out var story) && !RoadmapNeedsUpdate.IsRetired(story)).ToList();

        var now = DateTime.UtcNow;
        var open = visible.Where(item => item.Status == ChildRoadmapItemStatus.Open)
            .OrderBy(item => item.Id).ToList();
        var waiting = visible.Where(item => item.Status == ChildRoadmapItemStatus.Waiting)
            .OrderBy(item => item.Position)
            .ThenBy(item => item.Id)
            .ToList();
        var stageOrder = waiting.Select(item => item.Position).Distinct().OrderBy(position => position)
            .Select((position, index) => (position, order: index + 1))
            .ToDictionary(pair => pair.position, pair => pair.order);
        var completed = visible.Where(item => item.Status == ChildRoadmapItemStatus.Completed).ToList();

        return new MyRoadmapDto
        {
            OpenStories = open.Select(item => new MyRoadmapOpenStoryDto
            {
                StoryId = item.StoryId,
                Title = byId[item.StoryId].Title,
                CoverImageUrl = CoverOf(item.StoryId),
                Note = item.Note,
                ExpectedBy = item.ExpectedCompletionAt > now ? item.ExpectedCompletionAt : null,
                Stage = item.Position
            }).ToList(),
            Upcoming = waiting.Select(item => new MyRoadmapUpcomingDto
            {
                Order = stageOrder[item.Position],
                StoryId = item.StoryId,
                Title = byId[item.StoryId].Title,
                CoverImageUrl = CoverOf(item.StoryId),
                IsLocked = true
            }).ToList(),
            Done = completed
                .OrderByDescending(item => item.CompletedAt ?? item.CreatedAt)
                .ThenByDescending(item => item.Id)
                .Take(MyRoadmapPolicy.DoneLimit)
                .Select(item => new MyRoadmapDoneDto
                {
                    StoryId = item.StoryId,
                    Title = byId[item.StoryId].Title,
                    CoverImageUrl = CoverOf(item.StoryId),
                    CompletedAt = item.CompletedAt,
                    Stage = item.Position
                }).ToList(),
            CompletedCount = completed.Count,
            TotalCount = completed.Count + waiting.Count + open.Count,
            IsFinished = open.Count == 0 && waiting.Count == 0 && completed.Count > 0
        };
    }
}
