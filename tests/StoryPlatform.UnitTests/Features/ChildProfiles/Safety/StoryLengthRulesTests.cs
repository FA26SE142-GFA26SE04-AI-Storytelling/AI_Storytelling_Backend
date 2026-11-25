using StoryPlatform.Application.Features.ChildProfiles.Safety;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Safety;

public class StoryLengthRulesTests
{
    [Theory]
    [InlineData(900, 990)]
    [InlineData(1500, 1650)]
    [InlineData(1200, 1320)]
    [InlineData(101, 112)] // 101 × 1,1 = 111,1 → làm tròn lên
    public void UpperBound_IsTenPercentAboveTargetRoundedUp(int target, int expected) =>
        Assert.Equal(expected, StoryLengthRules.UpperBound(target));

    [Fact]
    public void CapFor_WithCustomTarget_RaisesCapButNeverLowersIt()
    {
        Assert.Equal(1320, StoryLengthRules.CapFor(1200, 900));
        Assert.Equal(2000, StoryLengthRules.CapFor(1200, 2000));
    }

    [Fact]
    public void CapFor_WithoutTarget_KeepsRequestedCap() =>
        Assert.Equal(700, StoryLengthRules.CapFor(null, 700));

    [Fact]
    public void ForGeneration_CustomTarget_UsesTargetAndRaisedCap() =>
        Assert.Equal((1200, 1320), StoryLengthRules.ForGeneration(1200, 900, 900));

    [Theory]
    [InlineData(2000, 900, 900, 2000)]   // trần rộng: giữ trần
    [InlineData(900, 900, 900, 990)]     // trần bằng mặc định: nâng lên 110%
    [InlineData(1500, 1500, 1500, 1650)] // bé 9–10 tuổi hoặc mức 4–5
    public void ForGeneration_FollowsLevelDefaultWhenNoTarget(int storedCap, int levelDefault, int expectedTarget, int expectedCap) =>
        Assert.Equal((expectedTarget, expectedCap), StoryLengthRules.ForGeneration(null, storedCap, levelDefault));

    [Fact]
    public void ForGeneration_LegacyCapBelowDefault_IsHonouredAsTargetAndCap() =>
        Assert.Equal((700, 700), StoryLengthRules.ForGeneration(null, 700, 900));
}
