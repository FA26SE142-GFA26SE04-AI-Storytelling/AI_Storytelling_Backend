using Moq;
using StoryPlatform.Application.Features.Moderation.Rubrics;
using StoryPlatform.Contracts.AI.Moderation;

namespace StoryPlatform.UnitTests.Features.Moderation;

internal static class RubricTestData
{
    public static RubricSnapshot Snapshot()
    {
        var definition = new RubricDefinition("Test-only database snapshot", GrayZoneRules.CriterionCodes.Select(code =>
            new RubricCriterion(code, code, "Check source evidence.", "Read complete context.",
                Enumerable.Range(0, 5).ToDictionary(x => x.ToString(), x => "Level " + x), [])).ToArray());
        return new(1, GrayZoneRules.RubricVersion, RubricRules.SchemaVersion, RubricRules.Hash(definition), definition);
    }
    public static IModerationRubricSource Source()
    {
        var source = new Mock<IModerationRubricSource>();
        source.Setup(x => x.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot());
        return source.Object;
    }
}
