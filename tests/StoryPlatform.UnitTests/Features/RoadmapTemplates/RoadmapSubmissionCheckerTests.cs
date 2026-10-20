using StoryPlatform.Application.Features.RoadmapTemplates.Interfaces;
using StoryPlatform.Application.Features.RoadmapTemplates.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.RoadmapTemplates;

public class RoadmapSubmissionCheckerTests
{
    private readonly RoadmapHarness _h = new();
    private readonly RoadmapSubmissionChecker _sut;

    public RoadmapSubmissionCheckerTests()
    {
        _sut = new RoadmapSubmissionChecker(_h.UnitOfWork.Object);
    }

    [Fact]
    public async Task EvaluateAsync_ValidRoadmap_ReturnsSixPassingChecksInStableOrder()
    {
        var roadmap = _h.AddRoadmap(
            1, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft,
            RoadmapHarness.ModeratorId, 10, 11);

        var checks = await _sut.EvaluateAsync(
            roadmap, _h.RoadmapItems.Where(item => item.ReadingRoadmapId == 1).ToList());

        Assert.Equal(new[]
        {
            RoadmapCheckCodes.NotEmpty,
            RoadmapCheckCodes.StoriesReady,
            RoadmapCheckCodes.AgeMatch,
            RoadmapCheckCodes.LevelInRange,
            RoadmapCheckCodes.NoDuplicateStories,
            RoadmapCheckCodes.ComboUnique
        }, checks.Select(check => check.Code));
        Assert.All(checks, check => Assert.True(check.Passed));
    }

    [Fact]
    public async Task EvaluateAsync_EmptyRoadmap_FailsNotEmpty()
    {
        var roadmap = _h.AddRoadmap(
            1, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft, RoadmapHarness.ModeratorId);

        var checks = await _sut.EvaluateAsync(roadmap, []);

        Assert.False(checks.Single(check => check.Code == RoadmapCheckCodes.NotEmpty).Passed);
        Assert.Equal(6, checks.Count);
    }

    [Fact]
    public async Task EvaluateAsync_IneligibleMissingAndArchivedStories_FailsStoriesReady()
    {
        var roadmap = _h.AddRoadmap(
            1, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft,
            RoadmapHarness.ModeratorId, 10, 999);
        _h.Stories.Single(story => story.Id == 10).ArchivedAt = DateTime.UtcNow;

        var check = (await _sut.EvaluateAsync(roadmap, _h.RoadmapItems))
            .Single(candidate => candidate.Code == RoadmapCheckCodes.StoriesReady);

        Assert.False(check.Passed);
        Assert.Contains("10", check.Message);
        Assert.Contains("999", check.Message);
    }

    [Fact]
    public async Task EvaluateAsync_WrongAgeAndLevel_FailsCorrespondingChecks()
    {
        var roadmap = _h.AddRoadmap(
            1, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft,
            RoadmapHarness.ModeratorId, 12);

        var checks = await _sut.EvaluateAsync(roadmap, _h.RoadmapItems);

        Assert.False(checks.Single(check => check.Code == RoadmapCheckCodes.AgeMatch).Passed);
        Assert.False(checks.Single(check => check.Code == RoadmapCheckCodes.LevelInRange).Passed);
    }

    [Fact]
    public async Task EvaluateAsync_DuplicateStory_FailsDuplicateCheck()
    {
        var roadmap = _h.AddRoadmap(
            1, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft,
            RoadmapHarness.ModeratorId, 10, 10);

        var check = (await _sut.EvaluateAsync(roadmap, _h.RoadmapItems))
            .Single(candidate => candidate.Code == RoadmapCheckCodes.NoDuplicateStories);

        Assert.False(check.Passed);
        Assert.Contains("10", check.Message);
    }

    [Fact]
    public async Task EvaluateAsync_PublishedComboExists_ReturnsRelatedRoadmap()
    {
        _h.AddRoadmap(
            9, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Published,
            RoadmapHarness.OtherModeratorId, 10);
        var roadmap = _h.AddRoadmap(
            1, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft,
            RoadmapHarness.ModeratorId, 10, 11);

        var check = (await _sut.EvaluateAsync(
                roadmap, _h.RoadmapItems.Where(item => item.ReadingRoadmapId == 1).ToList()))
            .Single(candidate => candidate.Code == RoadmapCheckCodes.ComboUnique);

        Assert.False(check.Passed);
        Assert.Equal(9, check.RelatedRoadmapId);
    }

    [Fact]
    public async Task EvaluateAsync_CloneDoesNotConflictWithItsSource()
    {
        _h.AddRoadmap(
            9, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Published,
            RoadmapHarness.OtherModeratorId, 10);
        var roadmap = _h.AddRoadmap(
            1, AgeBand.Age_6_8, 5, RoadmapTemplateStatus.Draft,
            RoadmapHarness.ModeratorId, 10, 11);
        roadmap.ClonedFromId = 9;

        var check = (await _sut.EvaluateAsync(
                roadmap, _h.RoadmapItems.Where(item => item.ReadingRoadmapId == 1).ToList()))
            .Single(candidate => candidate.Code == RoadmapCheckCodes.ComboUnique);

        Assert.True(check.Passed);
    }
}
