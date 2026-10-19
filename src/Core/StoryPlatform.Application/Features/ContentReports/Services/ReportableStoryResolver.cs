using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentReports;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentReports.Services;

public class ReportableStoryResolver : IReportableStoryResolver
{
    private const string NotAccessibleMessage =
        "Bạn không truy cập được truyện này qua trẻ nào mình giám sát.";

    private readonly IUnitOfWork _unitOfWork;

    public ReportableStoryResolver(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ReportableStory> ResolveAsync(
        int reporterUserId, int storyId, CancellationToken cancellationToken = default)
    {
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken);
        if (story == null || story.IsDeleted)
        {
            throw new NotFoundException("Truyện", storyId);
        }

        if (!story.IsPublished || story.Status == StoryStatus.Archived || story.ArchivedAt != null)
        {
            throw new ForbiddenException(NotAccessibleMessage);
        }

        var relationships = await _unitOfWork.Repository<SupervisionRelationship>().FindAsync(
            relationship => relationship.SupervisorUserId == reporterUserId && relationship.RevokedAt == null,
            cancellationToken: cancellationToken);
        var childIds = relationships.Select(relationship => relationship.ChildProfileId).Distinct().ToList();
        if (childIds.Count == 0)
        {
            throw new ForbiddenException(NotAccessibleMessage);
        }

        var children = await _unitOfWork.Repository<ChildProfile>().FindAsync(
            child => childIds.Contains(child.Id) && child.Status == ChildProfileStatus.Active,
            cancellationToken: cancellationToken);
        if (!children.Any(child => CanAccess(story, child)))
        {
            throw new ForbiddenException(NotAccessibleMessage);
        }

        var displayed = await ReportedContentBuilder.FindDisplayedVersionAsync(
            _unitOfWork, story, cancellationToken);
        return new ReportableStory(story, displayed?.Id);
    }

    private static bool CanAccess(Story story, ChildProfile child) =>
        story.Scope == StoryScope.Parent
            ? story.ChildProfileId == child.Id
            : AgeRank(story.AgeBand) <= (int)child.AgeBand;

    private static int AgeRank(string? ageBand) => ageBand switch
    {
        "6-8" => (int)AgeBand.Age_6_8,
        "9-10" => (int)AgeBand.Age_9_10,
        _ => int.MaxValue
    };

}
