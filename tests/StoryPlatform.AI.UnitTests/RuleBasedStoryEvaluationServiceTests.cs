using StoryPlatform.AI.Infrastructure.Evaluation;
using StoryPlatform.Contracts.AI.Models;
using Xunit;

namespace StoryPlatform.AI.UnitTests;

public sealed class RuleBasedStoryEvaluationServiceTests
{
    [Fact]
    public async Task Evaluate_RejectsBlockedTopic()
    {
        var service = new RuleBasedStoryEvaluationService();
        var story = new StoryPackageDto
        {
            Title = "The hidden weapon",
            StorySections = [new StorySectionDto(1, "Opening", "A child finds a weapon.")]
        };
        var constraints = new GenerationConstraintsDto { BlockedTopics = ["weapon"] };

        var result = await service.EvaluateAsync(story, constraints);

        Assert.False(result.SafetyPassed);
        Assert.Contains(result.Issues, issue => issue.Contains("blocked topic", StringComparison.OrdinalIgnoreCase));
    }
}
