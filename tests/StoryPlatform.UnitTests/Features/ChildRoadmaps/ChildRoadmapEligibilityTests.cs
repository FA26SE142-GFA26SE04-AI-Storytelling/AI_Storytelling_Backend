using StoryPlatform.Application.Features.ChildRoadmaps.Services;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildRoadmaps;

public class ChildRoadmapEligibilityTests
{
    private readonly ChildRoadmapHarness _h = new();
    private readonly ChildRoadmapEligibility _sut;

    public ChildRoadmapEligibilityTests()
    {
        _sut = new ChildRoadmapEligibility(_h.UnitOfWork.Object);
    }

    private StoryPlatform.Domain.Entities.ChildProfile Child =>
        _h.Children.Single(child => child.Id == ChildRoadmapHarness.Child);

    private StoryPlatform.Domain.Entities.Story Story => _h.Stories.Single(story => story.Id == 10);

    [Fact]
    public async Task ReadyStoryOfTheRightAge_IsEligible() =>
        Assert.Null(await _sut.EvaluateAsync(Child, Story));

    [Fact]
    public async Task NullOrDeletedStory_IsSourceDeleted()
    {
        Assert.Equal(ChildRoadmapSkipReason.SourceDeleted, await _sut.EvaluateAsync(Child, null));
        Story.IsDeleted = true;
        Assert.Equal(ChildRoadmapSkipReason.SourceDeleted, await _sut.EvaluateAsync(Child, Story));
    }

    [Theory]
    [InlineData("archived")]
    [InlineData("not-ready")]
    [InlineData("unpublished")]
    public async Task UnreadableStory_IsUnavailable(string problem)
    {
        if (problem == "archived") Story.ArchivedAt = DateTime.UtcNow;
        if (problem == "not-ready") Story.Status = StoryStatus.MediaProcessing;
        if (problem == "unpublished")
        {
            Story.Scope = StoryScope.Creator;
            Story.IsPublished = false;
        }

        Assert.Equal(ChildRoadmapSkipReason.StoryUnavailable, await _sut.EvaluateAsync(Child, Story));
    }

    [Fact]
    public async Task WrongAge_IsRejected()
    {
        Story.AgeBand = "9-10";
        Assert.Equal(ChildRoadmapSkipReason.WrongAge, await _sut.EvaluateAsync(Child, Story));
    }

    [Fact]
    public async Task BlockedTopic_IsRejected()
    {
        _h.Block(Child.Id, 5);
        _h.Tag(Story.Id, 5);
        Assert.Equal(ChildRoadmapSkipReason.BlockedTopic, await _sut.EvaluateAsync(Child, Story));
    }

    [Fact]
    public async Task StoryOverLengthLimit_IsRejected()
    {
        _h.AddPolicy(Child.Id, 100);
        Story.Content = new string('a', 101);
        Assert.Equal(ChildRoadmapSkipReason.TooLong, await _sut.EvaluateAsync(Child, Story));
    }

    [Fact]
    public async Task NoPolicy_DoesNotApplyTopicOrLengthRestrictions()
    {
        _h.Tag(Story.Id, 5);
        Story.Content = new string('a', 50000);
        Assert.Null(await _sut.EvaluateAsync(Child, Story));
    }
}
