using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningAgeSetsTests
{
    [Fact]
    public void Band6To8_GeneratesOnlyBuiltAges() => Assert.Equal([6], ListeningAgeSets.TargetAgesFor(AgeBand.Age_6_8));

    [Fact]
    public void Band9To10_GeneratesNothingUntilBuilt() => Assert.Empty(ListeningAgeSets.TargetAgesFor(AgeBand.Age_9_10));

    [Theory]
    [InlineData(6, AgeBand.Age_6_8, 6)]
    [InlineData(7, AgeBand.Age_6_8, 7)]
    [InlineData(10, AgeBand.Age_9_10, 10)]
    [InlineData(null, AgeBand.Age_6_8, 6)]
    [InlineData(null, AgeBand.Age_9_10, 9)]
    public void ChildAlwaysMapsToOwnAge(int? age, AgeBand band, int expected) =>
        Assert.Equal(expected, ListeningAgeSets.AgeFor(age, band));
}
