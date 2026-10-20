using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.RoadmapTemplates;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

public class MyRoadmapService : IMyRoadmapService
{
    private readonly IUnitOfWork _unitOfWork;

    public MyRoadmapService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
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

        var visible = items.Where(item =>
            byId.TryGetValue(item.StoryId, out var story) && !RoadmapNeedsUpdate.IsRetired(story)).ToList();

        var holdMode = await _unitOfWork.Repository<InterventionCase>().ExistsAsync(
            holdCase => holdCase.ChildProfileId == childProfileId
                        && !holdCase.IsDeleted
                        && holdCase.Status != InterventionStatus.ResolvedUnlocked,
            cancellationToken);

        var now = DateTime.UtcNow;
        var open = visible.FirstOrDefault(item => item.Status == ChildRoadmapItemStatus.Open);
        var waiting = visible.Where(item => item.Status == ChildRoadmapItemStatus.Waiting)
            .OrderBy(item => item.Position)
            .ThenBy(item => item.Id)
            .ToList();
        var completed = visible.Where(item => item.Status == ChildRoadmapItemStatus.Completed).ToList();

        return new MyRoadmapDto
        {
            Open = open == null
                ? null
                : new MyRoadmapOpenStoryDto
                {
                    StoryId = open.StoryId,
                    Title = byId[open.StoryId].Title,
                    CoverImageUrl = byId[open.StoryId].CoverImageUrl,
                    Note = open.Note,
                    ExpectedBy = open.ExpectedCompletionAt > now ? open.ExpectedCompletionAt : null,
                    IsLocked = holdMode
                },
            Upcoming = waiting.Select((item, index) => new MyRoadmapUpcomingDto
            {
                Order = index + 1,
                StoryId = item.StoryId,
                Title = byId[item.StoryId].Title,
                CoverImageUrl = byId[item.StoryId].CoverImageUrl,
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
                    CoverImageUrl = byId[item.StoryId].CoverImageUrl,
                    CompletedAt = item.CompletedAt
                }).ToList(),
            CompletedCount = completed.Count,
            TotalCount = completed.Count + waiting.Count + (open == null ? 0 : 1),
            IsFinished = open == null && waiting.Count == 0 && completed.Count > 0
        };
    }
}
