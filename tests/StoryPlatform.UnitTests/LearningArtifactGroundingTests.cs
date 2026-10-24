using System.Text.Json;
using StoryPlatform.Contracts.AI.Models;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class LearningArtifactGroundingTests
{
    private static StoryContentDto Story => new() { Title = "Na", StorySections = [new(1, "", "Na kiên nhẫn sửa xe. Nam tìm bánh.")] };
    private static LearningBatchSpec Spec => new() { TargetCount = 2, Kind = "vocabulary" };

    [Theory]
    [InlineData("Na", "Không có câu này.")]
    [InlineData("kiên", "Nam tìm bánh.")]
    [InlineData("an", "Nam tìm bánh.")]
    public void VocabularyRejectsInventedEvidenceMismatchedQuotesAndSubstringTerms(string term, string quote)
    {
        Assert.Throws<JsonException>(() => LearningArtifactGrounding.Vocabulary(Story,
            [new(term, "Nghĩa") { SourceQuote = quote }], Spec, "Insufficient source material"));
    }
    [Fact]
    public void ShortfallIsAllowedOnlyWithAReasonAndGroundedDistinctTerms()
    {
        GeneratedVocabularyItemDto[] items = [new("kiên nhẫn", "Không bỏ cuộc ngay") { SourceQuote = "Na kiên nhẫn sửa xe." }];
        Assert.Throws<JsonException>(() => LearningArtifactGrounding.Vocabulary(Story, items, Spec, ""));
        LearningArtifactGrounding.Vocabulary(Story, items, Spec, "Only one suitable term for this objective");
        Assert.Throws<JsonException>(() => LearningArtifactGrounding.Vocabulary(Story, [items[0], items[0]], Spec, ""));
        Assert.Equal(7, StoryWordCounter.Count("Na kiên nhẫn sửa xe. Nam tìm."));
    }
    [Theory]
    [InlineData("6-8", 810, 990)]
    [InlineData("9-10", 1350, 1650)]
    public void PinnedAgeBoundsAreNotTheLegacySeventyPercentMinimum(string age, int minimum, int maximum)
    {
        var profile = LearningGenerationProfile.ForAge(age);
        Assert.Equal(minimum, profile.MinimumWords); Assert.Equal(maximum, profile.MaximumWords);
    }
}
