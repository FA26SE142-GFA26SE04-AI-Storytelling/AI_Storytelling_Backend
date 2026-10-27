using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.RoadmapTemplates;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Contracts.AI.Models;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

public class ChildRoadmapEligibility : IChildRoadmapEligibility
{
    private readonly IUnitOfWork _unitOfWork;

    public ChildRoadmapEligibility(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<ChildRoadmapSkipReason?> EvaluateAsync(
        ChildProfile child, Story? story, CancellationToken cancellationToken = default) =>
        EvaluateCoreAsync(child, story, false, cancellationToken);

    public Task<ChildRoadmapSkipReason?> EvaluateForParentSelectionAsync(
        ChildProfile child, Story? story, CancellationToken cancellationToken = default) =>
        EvaluateCoreAsync(child, story, true, cancellationToken);

    private async Task<ChildRoadmapSkipReason?> EvaluateCoreAsync(
        ChildProfile child, Story? story, bool parentSelected, CancellationToken cancellationToken)
    {
        if (story == null || story.IsDeleted)
        {
            return ChildRoadmapSkipReason.SourceDeleted;
        }

        // Family editor packages are delivered only through pinned family confirmations.
        // Ready media must not open the legacy roadmap reader for this separate workflow.
        if (StoryPlatform.Application.Common.ParentStoryContext.IsEditorStory(story))
            return ChildRoadmapSkipReason.StoryUnavailable;

        if (story.Status != StoryStatus.Ready
            || story.ArchivedAt != null
            || (story.Scope == StoryScope.Creator && !story.IsPublished))
        {
            return ChildRoadmapSkipReason.StoryUnavailable;
        }

        if (story.AgeBand != RoadmapAge.ToText(child.AgeBand) && !parentSelected
            && !await ParentStorySelection.ExistsAsync(_unitOfWork, child.Id, story.Id, cancellationToken))
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

        if (policy.MaxStoryLength > 0 && StoryWordCounter.Count(story.Content ?? string.Empty) > policy.MaxStoryLength)
        {
            return ChildRoadmapSkipReason.TooLong;
        }

        return null;
    }
}
