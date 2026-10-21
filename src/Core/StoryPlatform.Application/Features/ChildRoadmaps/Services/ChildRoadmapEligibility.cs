using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.RoadmapTemplates;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

public class ChildRoadmapEligibility : IChildRoadmapEligibility
{
    private readonly IUnitOfWork _unitOfWork;

    public ChildRoadmapEligibility(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ChildRoadmapSkipReason?> EvaluateAsync(
        ChildProfile child, Story? story, CancellationToken cancellationToken = default)
    {
        if (story == null || story.IsDeleted)
        {
            return ChildRoadmapSkipReason.SourceDeleted;
        }

        if (story.Status != StoryStatus.Ready
            || story.ArchivedAt != null
            || (story.Scope == StoryScope.Creator && !story.IsPublished))
        {
            return ChildRoadmapSkipReason.StoryUnavailable;
        }

        if (story.AgeBand != RoadmapAge.ToText(child.AgeBand))
        {
            return ChildRoadmapSkipReason.WrongAge;
        }

        var childId = child.Id;
        var policy = await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            existing => existing.ChildProfileId == childId && !existing.IsDeleted,
            cancellationToken: cancellationToken);
        if (policy == null)
        {
            return null;
        }

        var policyId = policy.Id;
        var rules = await _unitOfWork.Repository<SafetyPolicyCategory>().FindAsync(
            rule => rule.SafetyPolicyId == policyId, cancellationToken: cancellationToken);
        var storyId = story.Id;
        var links = await _unitOfWork.Repository<StoryCategory>().FindAsync(
            link => link.StoryId == storyId, cancellationToken: cancellationToken);
        if (links.Any(link => !SafetyPolicyEvaluator.IsCategoryPermitted(link.ContentCategoryId, rules)))
        {
            return ChildRoadmapSkipReason.BlockedTopic;
        }

        if (policy.MaxStoryLength > 0 && (story.Content?.Length ?? 0) > policy.MaxStoryLength)
        {
            return ChildRoadmapSkipReason.TooLong;
        }

        return null;
    }
}
