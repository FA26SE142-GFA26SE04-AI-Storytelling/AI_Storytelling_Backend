using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.ContentGeneration.Quality;
using StoryPlatform.Contracts.AI.Models;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class RuleBasedContentQualityEvaluatorTests
{
    [Fact]
    public void Headings_cannot_make_a_passing_body_fail_only_after_generation()
    {
        var body = string.Join(' ', Enumerable.Repeat("Lan và Minh cùng chia sẻ sách rồi đi.", 12));
        var story = Story(body) with
        {
            StorySections = [new StorySectionDto(1, string.Join(' ', Enumerable.Repeat("Lan và Minh", 12)), body)]
        };
        var context = Context() with { ReadingLevel = 3, ReadabilityScoreThreshold = 70m };
        Assert.True(ReadabilityCalculator.EvaluateForProfile(body, "vi", 3, 70m).Passed);

        var generated = new RuleBasedContentQualityEvaluator().Evaluate(story,
            new StoryOutlineDto("Lan gặp Minh", "Hai bạn chia sẻ", "Hai bạn cùng đọc sách"), Input(), context);
        var reviewed = ReadabilityCalculator.EvaluateForProfile(CanonicalStoryContent.Render(story), "vi", 3, 70m);

        Assert.False(reviewed.Passed);
        Assert.False(generated.Readability.Passed);
        Assert.False(generated.IsPassed);
        Assert.Equal(reviewed.Metrics.Fre, generated.ReadabilityDetails!.Ease);
        Assert.Equal(reviewed.Metrics.Fkgl, generated.ReadabilityDetails.Grade);
        Assert.Equal(reviewed.Metrics.AverageWordsPerSentence, generated.ReadabilityDetails.AverageWordsPerSentence);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    public void Improving_ease_does_not_bypass_the_body_length_gate(int sentences)
    {
        var result = new RuleBasedContentQualityEvaluator().Evaluate(
            Story(string.Join(' ', Enumerable.Repeat("Lan và Minh cùng chia sẻ sách.", sentences))),
            new StoryOutlineDto("Lan gặp Minh", "Hai bạn chia sẻ", "Hai bạn cùng đọc sách"), Input(),
            Context() with { ReadingLevel = 3, ReadabilityScoreThreshold = 70m });
        Assert.True(result.Readability.Passed);
        Assert.False(result.Length.Passed);
        Assert.False(result.IsPassed);
    }

    [Fact]
    public void Readability_feedback_identifies_ease_only_and_preserves_snapshot_threshold()
    {
        var context = Context() with { ReadingLevel = 3, ReadabilityScoreThreshold = 70m };
        var result = new RuleBasedContentQualityEvaluator().Evaluate(
            Story(string.Join(' ', Enumerable.Repeat("Lan và Minh cùng chia sẻ sách rồi đi chơi ở sân.", 12))),
            new StoryOutlineDto("Lan gặp Minh", "Hai bạn chia sẻ", "Hai bạn cùng đọc sách"), Input(), context);
        Assert.Equal(new[] { "ease" }, result.ReadabilityDetails!.FailedMetrics);
        Assert.Equal(70m, result.ReadabilityDetails.Limits.MinimumEaseScore);
        Assert.Equal(8, result.ReadabilityDetails.SentenceTarget);
        Assert.False(result.Readability.Passed);
    }

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
