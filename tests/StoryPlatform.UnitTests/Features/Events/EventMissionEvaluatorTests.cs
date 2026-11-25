using System;
using System.Collections.Generic;
using StoryPlatform.Application.Features.Events.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Events;

public class EventMissionEvaluatorTests
{
    [Fact]
    public void Evaluate_AllMandatory_SupportsArbitraryTaskCountAndMultiChildWithoutDoubleCountingStory()
    {
        var rule = new EventRuleVersion
        {
            Id = 1,
            CompletionMode = TrackCompletionMode.AllMandatory
        };

        var tasks = new List<EventTaskDefinition>
        {
            new() { Id = 1, TaskCode = "T01", VerifierType = TaskVerifierType.ReadEventStories, TargetValue = 3, IsMandatory = true, Points = 20, SortOrder = 1 },
            new() { Id = 2, TaskCode = "T02", VerifierType = TaskVerifierType.GuidedDiscussionCheckin, TargetValue = 1, IsMandatory = true, Points = 10, SortOrder = 2 },
            new() { Id = 3, TaskCode = "T03", VerifierType = TaskVerifierType.RealLifePracticeCheckin, TargetValue = 1, IsMandatory = true, Points = 10, SortOrder = 3 },
            new() { Id = 4, TaskCode = "T04", VerifierType = TaskVerifierType.RealLifePracticeCheckin, TargetValue = 1, IsMandatory = true, Points = 10, SortOrder = 4 },
            new() { Id = 5, TaskCode = "T05", VerifierType = TaskVerifierType.ActiveDaysCount, TargetValue = 2, IsMandatory = true, Points = 15, SortOrder = 5 },
            new() { Id = 6, TaskCode = "T06", VerifierType = TaskVerifierType.RateAndReviewStory, TargetValue = 1, IsMandatory = false, Points = 5, SortOrder = 6 },
        };

        // Child A & Child B both read Story 101 -> only counts once toward DISTINCT StoryId (BR-CP03).
        var progresses = new List<EventTaskProgress>
        {
            new() { Id = 1, TaskDefinitionId = 1, ChildProfileId = 10, StoryId = 101, ActivityLocalDate = new DateOnly(2026, 11, 20) },
            new() { Id = 2, TaskDefinitionId = 1, ChildProfileId = 11, StoryId = 101, ActivityLocalDate = new DateOnly(2026, 11, 20) },
            new() { Id = 3, TaskDefinitionId = 1, ChildProfileId = 11, StoryId = 102, ActivityLocalDate = new DateOnly(2026, 11, 21) },
            new() { Id = 4, TaskDefinitionId = 2, StoryId = 101, ActivityLocalDate = new DateOnly(2026, 11, 20) },
            new() { Id = 5, TaskDefinitionId = 3, StoryId = 101, ActivityLocalDate = new DateOnly(2026, 11, 20) },
            new() { Id = 6, TaskDefinitionId = 4, StoryId = 102, ActivityLocalDate = new DateOnly(2026, 11, 21) },
            new() { Id = 7, TaskDefinitionId = 5, ActivityLocalDate = new DateOnly(2026, 11, 20) },
            new() { Id = 8, TaskDefinitionId = 5, ActivityLocalDate = new DateOnly(2026, 11, 21) },
        };

        var beforeThirdStory = EventMissionEvaluator.Evaluate(rule, tasks, progresses);
        Assert.False(beforeThirdStory.AllRequirementsMet);
        Assert.Equal(2, beforeThirdStory.Tasks[0].CurrentValue);
        Assert.Equal(4, beforeThirdStory.CompletedCount);

        progresses.Add(new EventTaskProgress { Id = 9, TaskDefinitionId = 1, ChildProfileId = 10, StoryId = 103, ActivityLocalDate = new DateOnly(2026, 11, 21) });
        var afterThirdStory = EventMissionEvaluator.Evaluate(rule, tasks, progresses);
        Assert.True(afterThirdStory.AllRequirementsMet);
        Assert.Equal(5, afterThirdStory.CompletedCount);
        Assert.Equal(65, afterThirdStory.EarnedPoints);
    }

    [Fact]
    public void Evaluate_MinimumTasksCount_And_MinimumPointsThreshold_WorkDynamically()
    {
        var tasks = new List<EventTaskDefinition>
        {
            new() { Id = 1, TaskCode = "T01", VerifierType = TaskVerifierType.GuidedDiscussionCheckin, TargetValue = 1, IsMandatory = true, Points = 20 },
            new() { Id = 2, TaskCode = "T02", VerifierType = TaskVerifierType.RealLifePracticeCheckin, TargetValue = 1, IsMandatory = false, Points = 30 },
            new() { Id = 3, TaskCode = "T03", VerifierType = TaskVerifierType.RealLifePracticeCheckin, TargetValue = 1, IsMandatory = false, Points = 50 },
        };

        var progresses = new List<EventTaskProgress>
        {
            new() { Id = 1, TaskDefinitionId = 1, StoryId = 101, ActivityLocalDate = new DateOnly(2026, 11, 20) },
            new() { Id = 2, TaskDefinitionId = 3, StoryId = 101, ActivityLocalDate = new DateOnly(2026, 11, 20) },
        };

        var countRule = new EventRuleVersion { CompletionMode = TrackCompletionMode.MinimumTasksCount, RequiredTaskCount = 2 };
        Assert.True(EventMissionEvaluator.Evaluate(countRule, tasks, progresses).AllRequirementsMet);

        var pointsRule = new EventRuleVersion { CompletionMode = TrackCompletionMode.MinimumPointsThreshold, RequiredPoints = 80 };
        Assert.False(EventMissionEvaluator.Evaluate(pointsRule, tasks, progresses).AllRequirementsMet);

        var pointsRuleMet = new EventRuleVersion { CompletionMode = TrackCompletionMode.MinimumPointsThreshold, RequiredPoints = 70 };
        Assert.True(EventMissionEvaluator.Evaluate(pointsRuleMet, tasks, progresses).AllRequirementsMet);
        pointsRuleMet.RequiredPoints = 50;
        Assert.True(EventMissionEvaluator.Evaluate(pointsRuleMet, tasks, [progresses[1]]).AllRequirementsMet);
    }

    [Fact]
    public void RankCreatorSubmissions_AppliesDeterministicTieBreaker()
    {
        var t1 = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddHours(1);
        var ranked = EventMissionEvaluator.RankCreatorSubmissions(new[]
        {
            new CreatorRankingCandidate(1, 11, 90m, 25m, 1, t1),
            new CreatorRankingCandidate(2, 12, 90m, 28m, 2, t2),
            new CreatorRankingCandidate(3, 13, 90m, 28m, 0, t2),
        });

        Assert.Equal(new[] { 3, 2, 1 }, new[] { ranked[0].SubmissionId, ranked[1].SubmissionId, ranked[2].SubmissionId });
    }

    [Fact]
    public void ComputeJaccardSimilarity_DetectsHighOverlap()
    {
        var textA = "Ngày xửa ngày xưa có một chú ếch xanh sống ở đầm sen yên bình.";
        var textB = "Ngày xửa ngày xưa có một chú ếch xanh sống ở đầm sen rất đẹp.";
        var textC = "Tàu vũ trụ bay qua các vì sao lấp lánh trên bầu trời đêm.";

        Assert.True(EventMissionEvaluator.ComputeJaccardSimilarity(textA, textB) > 0.5);
        Assert.True(EventMissionEvaluator.ComputeJaccardSimilarity(textA, textC) < 0.1);
    }
}
