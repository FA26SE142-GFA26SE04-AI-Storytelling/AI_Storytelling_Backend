using StoryPlatform.Application.Features.ChildProfiles.Learning;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Learning;

public class ReadingModeDefaultsTests
{
    [Fact]
    public void AgeBand_MatchesSpecValuesOnly()
    {
        Assert.Equal(new[] { "Age_6_8", "Age_9_10" }, Enum.GetNames<AgeBand>());
    }

    [Theory]
    [InlineData(AgeBand.Age_6_8, ReadingMode.ListenFirst)]
    [InlineData(AgeBand.Age_9_10, ReadingMode.ReadFirst)]
    public void ForAgeBand_ReturnsSpecDefault(AgeBand ageBand, ReadingMode expected) =>
        Assert.Equal(expected, ReadingModeDefaults.ForAgeBand(ageBand));
}
