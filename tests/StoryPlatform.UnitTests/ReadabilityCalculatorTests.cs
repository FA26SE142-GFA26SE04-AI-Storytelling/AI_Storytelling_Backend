using StoryPlatform.Application.Features.ContentGeneration.Quality;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class ReadabilityCalculatorTests
{
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
