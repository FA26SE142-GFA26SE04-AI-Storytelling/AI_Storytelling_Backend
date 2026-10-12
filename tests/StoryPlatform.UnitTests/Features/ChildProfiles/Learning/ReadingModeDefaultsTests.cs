using StoryPlatform.Application.Features.ChildProfiles.Learning;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Learning;

public class ReadingModeDefaultsTests
{
    [Theory]
    [InlineData(AgeBand.Age_6_8, ReadingMode.ListenFirst)]
    [InlineData(AgeBand.Age_9_12, ReadingMode.ReadFirst)]
    public void ForAgeBand_ReturnsSpecDefault(AgeBand ageBand, ReadingMode expected) =>
        Assert.Equal(expected, ReadingModeDefaults.ForAgeBand(ageBand));
}
