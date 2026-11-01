using System.Text;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class ReadAloudMatcherTests
{
    private static IReadOnlyList<RecognizedWord> Heard(params string[] words) =>
        words.Select(word => new RecognizedWord(word, 0.9)).ToList();

    private static string[] Statuses(IReadOnlyList<ReadAloudWordDto> result) =>
        result.Select(word => word.Status).ToArray();

    [Fact]
    public void ExactReading_IsAllGood_AndIgnoresCaseAndPunctuation()
    {
        var result = ReadAloudMatcher.Match("Thỏ con ra vườn.", Heard("thỏ", "con", "ra", "vườn"));

        Assert.Equal(new[] { "Thỏ", "con", "ra", "vườn" }, result.Select(word => word.Text));
        Assert.All(result, word => Assert.Equal(ReadAloudWordStatus.Good, word.Status));
        Assert.Equal(new[] { 0, 1, 2, 3 }, result.Select(word => word.Index));
    }

    [Fact]
    public void WrongTone_IsRetryWithToneHint()
    {
        var result = ReadAloudMatcher.Match("Thỏ con ra vườn", Heard("thỏ", "còn", "ra", "vườn"));

        Assert.Equal(new[] { "good", "retry", "good", "good" }, Statuses(result));
        Assert.Equal(ReadAloudHint.Tone, result[1].Hint);
    }

    [Fact]
    public void DifferentWord_IsRetryWithDifferentHint()
    {
        var result = ReadAloudMatcher.Match("Thỏ con ra vườn", Heard("thỏ", "mèo", "ra", "vườn"));

        Assert.Equal(new[] { "good", "retry", "good", "good" }, Statuses(result));
        Assert.Equal(ReadAloudHint.Different, result[1].Hint);
    }

    [Fact]
    public void MissingWord_IsSkipped()
    {
        var result = ReadAloudMatcher.Match("Thỏ con ra vườn", Heard("thỏ", "ra", "vườn"));

        Assert.Equal(new[] { "good", "skipped", "good", "good" }, Statuses(result));
        Assert.Null(result[1].Hint);
    }

    [Fact]
    public void ExtraSpokenWord_IsIgnored()
    {
        var result = ReadAloudMatcher.Match("Thỏ con ra vườn", Heard("à", "thỏ", "con", "ra", "vườn"));

        Assert.Equal(new[] { "good", "good", "good", "good" }, Statuses(result));
    }

    [Fact]
    public void LowConfidenceMatch_IsRetryWithUnclearHint()
    {
        var spoken = new List<RecognizedWord>
        {
            new("thỏ", 0.95), new("con", 0.2), new("ra", 0.9), new("vườn", null)
        };

        var result = ReadAloudMatcher.Match("Thỏ con ra vườn", spoken);

        Assert.Equal(new[] { "good", "retry", "good", "good" }, Statuses(result));
        Assert.Equal(ReadAloudHint.Unclear, result[1].Hint);
    }

    [Fact]
    public void NothingHeard_IsAllSkipped()
    {
        var result = ReadAloudMatcher.Match("Thỏ con ra vườn", Array.Empty<RecognizedWord>());

        Assert.All(result, word => Assert.Equal(ReadAloudWordStatus.Skipped, word.Status));
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void DecomposedUnicode_MatchesPrecomposedText()
    {
        var decomposed = "vườn".Normalize(NormalizationForm.FormD);

        var result = ReadAloudMatcher.Match("vườn", Heard(decomposed));

        Assert.Equal(ReadAloudWordStatus.Good, Assert.Single(result).Status);
    }

    [Fact]
    public void SpokenPhraseWithSpaces_IsSplitIntoWords()
    {
        var result = ReadAloudMatcher.Match("cà rốt thật to", Heard("cà rốt", "thật to"));

        Assert.Equal(new[] { "good", "good", "good", "good" }, Statuses(result));
    }
}
