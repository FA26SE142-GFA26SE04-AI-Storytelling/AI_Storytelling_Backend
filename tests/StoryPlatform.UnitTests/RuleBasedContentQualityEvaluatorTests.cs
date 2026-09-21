using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.ContentGeneration.Quality;
using StoryPlatform.Contracts.AI.Models;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class RuleBasedContentQualityEvaluatorTests
{
    [Fact]
    public void Safe_story_with_expected_length_passes_content_only_gates()
    {
        var result = new RuleBasedContentQualityEvaluator().Evaluate(
            Story(string.Join(' ', Enumerable.Repeat("Lan và Minh cùng chia sẻ sách.", 20))),
            new StoryOutlineDto("Lan gặp Minh", "Hai bạn chia sẻ", "Hai bạn cùng đọc sách"), Input(), Context());

        Assert.True(result.IsPassed);
    }

    [Fact]
    public void Blocked_policy_term_is_hard_failure()
    {
        var context = Context() with { BlockedCategoryTerms = ["bạo lực"] };
        var result = new RuleBasedContentQualityEvaluator().Evaluate(
            Story(string.Join(' ', Enumerable.Repeat("Lan và Minh cùng chia sẻ sách nhưng có bạo lực.", 15))),
            new StoryOutlineDto("Lan gặp Minh", "Hai bạn chia sẻ", "Hai bạn cùng đọc sách"), Input(), context);

        Assert.False(result.Safety.Passed);
        Assert.False(result.Safety.CanRefine);
        Assert.Equal("CONTENT_SAFETY_BLOCKED", result.Safety.ReasonCode);
    }

    [Fact]
    public void Configured_readability_threshold_is_applied_to_quality_gate()
    {
        var context = Context() with { ReadabilityScoreThreshold = 99m };
        var result = new RuleBasedContentQualityEvaluator().Evaluate(
            Story(string.Join(' ', Enumerable.Repeat("Lan và Minh cùng chia sẻ sách.", 20))),
            new StoryOutlineDto("Lan gặp Minh", "Hai bạn chia sẻ", "Hai bạn cùng đọc sách"), Input(), context);

        Assert.False(result.Readability.Passed);
        Assert.Equal("CONTENT_READABILITY_NOT_MET", result.Readability.ReasonCode);
    }

    private static StoryContentDto Story(string content) => new()
    {
        Title = "Tình bạn", Lesson = "Biết chia sẻ",
        StorySections = [new StorySectionDto(1, "Câu chuyện", content)]
    };
    private static AcceptedAIStoryInputSnapshot Input() =>
        new("Tình bạn", null, "ai_suggested", [], "ai_suggested", null, "Biết chia sẻ", "level_2", "vi", 100);
    private static AIStoryInputContextSnapshot Context() =>
        new(1, "6-8", 2, "level_2", "vi", 200, "always_manual", [], [], [], []);
}
