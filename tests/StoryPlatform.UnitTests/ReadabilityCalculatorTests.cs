using StoryPlatform.Application.Features.ContentGeneration.Quality;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class ReadabilityCalculatorTests
{
    [Theory]
    [InlineData(45, 12)]
    [InlineData(70, 8)]
    [InlineData(80, 5)]
    public void Vietnamese_sentence_guidance_accounts_for_configured_ease(int ease, int target)
    {
        var limits = StoryPlatform.Contracts.AI.Models.ContentReadabilityLimits.ForLevel(3, ease);
        Assert.Equal(target, limits.SentenceTarget("vi-VN"));
        Assert.Equal(12, limits.SentenceTarget("en"));
        Assert.Equal(ease, limits.MinimumEaseScore);
    }

    [Fact]
    public void Ease_can_fail_even_when_grade_and_sentence_length_pass()
    {
        var result = ReadabilityCalculator.EvaluateForProfile(
            string.Join(' ', Enumerable.Repeat("Lan và Minh cùng chia sẻ sách rồi đi chơi ở sân.", 75)), "vi", 3, 70m);
        Assert.True(result.Metrics.Fkgl <= result.MaximumGradeLevel);
        Assert.True(result.Metrics.AverageWordsPerSentence <= result.MaximumAverageWordsPerSentence);
        Assert.True(result.Metrics.Fre < result.MinimumEaseScore);
        Assert.False(result.Passed);
    }

    [Fact]
    public void Vietnamese_content_produces_versioned_grade_and_ease_metrics()
    {
        var result = ReadabilityCalculator.Calculate(
            "Lan gặp Minh trong rừng. Hai bạn cùng đọc sách và giúp đỡ nhau.",
            "vi");

        Assert.Equal("VI_READABILITY_V1", result.Algorithm);
        Assert.NotNull(result.Fkgl);
        Assert.NotNull(result.Fre);
        Assert.InRange(result.Fre!.Value, 0m, 100m);
    }

    [Fact]
    public void English_content_uses_standard_fkgl_fre_algorithm()
    {
        var result = ReadabilityCalculator.Calculate(
            "The cat sits by the tree. The child reads a short book.",
            "en");

        Assert.Equal("FKGL_FRE_EN_V1", result.Algorithm);
        Assert.NotNull(result.Fkgl);
        Assert.NotNull(result.Fre);
    }

    [Fact]
    public void Profile_threshold_can_reject_otherwise_simple_vietnamese_content()
    {
        const string content = "Lan gặp Minh trong rừng. Hai bạn cùng đọc sách.";

        var defaultResult = ReadabilityCalculator.EvaluateForProfile(content, "vi", 2);
        var strictResult = ReadabilityCalculator.EvaluateForProfile(content, "vi", 2, 99m);

        Assert.True(defaultResult.Passed);
        Assert.False(strictResult.Passed);
        Assert.Equal(99m, strictResult.MinimumEaseScore);
    }

    [Fact]
    public void Empty_content_cannot_pass_profile_gate()
    {
        var result = ReadabilityCalculator.EvaluateForProfile(string.Empty, "vi", 2);

        Assert.False(result.Passed);
        Assert.Null(result.Metrics.Fkgl);
        Assert.Null(result.Metrics.Fre);
    }
}
