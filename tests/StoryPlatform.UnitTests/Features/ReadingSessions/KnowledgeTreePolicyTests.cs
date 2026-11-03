using StoryPlatform.Application.Features.ReadingSessions;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class KnowledgeTreePolicyTests
{
    [Theory]
    [InlineData(0, KnowledgeTreePolicy.Seed, 30)]
    [InlineData(29, KnowledgeTreePolicy.Seed, 30)]
    [InlineData(30, KnowledgeTreePolicy.Sprout, 100)]
    [InlineData(100, KnowledgeTreePolicy.Sapling, 250)]
    [InlineData(250, KnowledgeTreePolicy.Grown, 500)]
    [InlineData(499, KnowledgeTreePolicy.Grown, 500)]
    public void StageOf_ReturnsTheStageAndTheNextThreshold(int exp, string code, int nextAt)
    {
        var stage = KnowledgeTreePolicy.StageOf(exp);

        Assert.Equal(code, stage.Code);
        Assert.Equal(nextAt, stage.NextAt);
    }

    [Fact]
    public void StageOf_AtFlowerThreshold_IsFloweringWithNoNextStage()
    {
        var stage = KnowledgeTreePolicy.StageOf(KnowledgeTreePolicy.FlowerAt);

        Assert.Equal(KnowledgeTreePolicy.Flowering, stage.Code);
        Assert.Null(stage.NextAt);
    }
}
