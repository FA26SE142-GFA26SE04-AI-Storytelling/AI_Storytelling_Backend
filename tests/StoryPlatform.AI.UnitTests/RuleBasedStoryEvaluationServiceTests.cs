using StoryPlatform.AI.Infrastructure.Evaluation;
using StoryPlatform.Contracts.AI.Models;
using Xunit;

namespace StoryPlatform.AI.UnitTests;

public sealed class RuleBasedStoryEvaluationServiceTests
{
    [Fact]
    public async Task Evaluation_does_not_request_discussion_generation_or_refinement()
    {
        var result = await new RuleBasedStoryEvaluationService().EvaluateAsync(
            new StoryPackageDto(), new GenerationConstraintsDto());

        Assert.True(result.DiscussionPassed);
        Assert.DoesNotContain(result.IssueDetails, x => x.Stage == "discussion");
        Assert.DoesNotContain(result.Issues, x => x.Contains("discussion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Legacy_discussion_result_is_not_an_evaluation_gate()
    {
        var result = new EvaluationResultDto
        {
            SchemaValid = true, SafetyPassed = true, ReadabilityPassed = true,
            VocabularyPassed = true, QuizPassed = true, DiscussionPassed = false
        };

        Assert.True(result.Passed);
        Assert.Equal("passed", result.Verdict);
    }

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
