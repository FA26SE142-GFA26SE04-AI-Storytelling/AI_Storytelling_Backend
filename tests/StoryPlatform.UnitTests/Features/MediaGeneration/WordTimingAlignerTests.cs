using System.Text.Json;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using Xunit;

namespace StoryPlatform.UnitTests.Features.MediaGeneration;

public sealed class WordTimingAlignerTests
{
    private static IReadOnlyList<TimedWord> Heard(params (string W, double S, double E)[] words) =>
        words.Select(w => new TimedWord(w.W, w.S, w.E)).ToList();

    private static List<(string Word, double Start, double? End)> Parse(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateArray()
            .Select(e => (e.GetProperty("word").GetString()!, e.GetProperty("start").GetDouble(),
                e.GetProperty("end").ValueKind == JsonValueKind.Null ? (double?)null : e.GetProperty("end").GetDouble()))
            .ToList();

    [Fact]
    public void ExactMatch_GivesEachSourceWordItsHeardTiming()
    {
        var tokens = SsmlTokenizer.Tokenize("Thỏ con đi lạc.");
        var json = WordTimingAligner.BuildWordTimingsJson(tokens,
            Heard(("thỏ", 0.0, 0.4), ("con", 0.4, 0.7), ("đi", 0.7, 0.9), ("lạc", 0.9, 1.3)), 0.8);

        var timings = Parse(json!);
        Assert.Equal(["Thỏ", "con", "đi", "lạc"], timings.Select(t => t.Word));
        Assert.Equal([0.0, 0.4, 0.7, 0.9], timings.Select(t => t.Start));
        Assert.Equal([0.4, 0.7, 0.9, 1.3], timings.Select(t => t.End!.Value));
    }

    [Fact]
    public void CasePunctuationAndNormalizationForm_AreIgnoredWhenMatching()
    {
        var tokens = SsmlTokenizer.Tokenize("Thỏ, con!");
        var decomposed = "Thỏ".Normalize(System.Text.NormalizationForm.FormD);
        var json = WordTimingAligner.BuildWordTimingsJson(tokens,
            Heard((decomposed.ToUpperInvariant(), 0.0, 0.5), ("CON.", 0.5, 0.9)), 1.0);

        Assert.NotNull(json);
        Assert.Equal(2, Parse(json).Count);
    }

    [Fact]
    public void RepeatedWords_AreMatchedInOrder()
    {
        var tokens = SsmlTokenizer.Tokenize("ơi ơi ơi");
        var json = WordTimingAligner.BuildWordTimingsJson(tokens,
            Heard(("ơi", 0.0, 0.3), ("ơi", 0.5, 0.8), ("ơi", 1.0, 1.3)), 1.0);

        Assert.Equal([0.0, 0.5, 1.0], Parse(json!).Select(t => t.Start));
    }

    [Fact]
    public void UnheardWordBetweenTwoMatches_IsInterpolatedInsideTheGap()
    {
        var tokens = SsmlTokenizer.Tokenize("thỏ con đi lạc");
        var json = WordTimingAligner.BuildWordTimingsJson(tokens,
            Heard(("thỏ", 0.0, 0.4), ("đi", 1.0, 1.2), ("lạc", 1.2, 1.6)), 0.7);

        var timings = Parse(json!);
        Assert.Equal(4, timings.Count);
        Assert.InRange(timings[1].Start, 0.4, 1.0);
        Assert.InRange(timings[1].End!.Value, timings[1].Start, 1.0);
    }

    [Fact]
    public void ExtraHeardWord_IsIgnored()
    {
        var tokens = SsmlTokenizer.Tokenize("thỏ con");
        var json = WordTimingAligner.BuildWordTimingsJson(tokens,
            Heard(("thỏ", 0.0, 0.4), ("ừm", 0.4, 0.6), ("con", 0.6, 0.9)), 1.0);

        Assert.Equal([0.0, 0.6], Parse(json!).Select(t => t.Start));
    }

    [Fact]
    public void CoverageBelowMinimum_ReturnsNull()
    {
        var tokens = SsmlTokenizer.Tokenize("thỏ con đi lạc vào rừng");
        var json = WordTimingAligner.BuildWordTimingsJson(tokens, Heard(("thỏ", 0.0, 0.4), ("rừng", 2.0, 2.4)), 0.8);

        Assert.Null(json);
    }

    [Fact]
    public void NothingHeardOrNoSourceWords_ReturnsNull()
    {
        Assert.Null(WordTimingAligner.BuildWordTimingsJson(SsmlTokenizer.Tokenize("thỏ con"), Heard(), 0.5));
        Assert.Null(WordTimingAligner.BuildWordTimingsJson(SsmlTokenizer.Tokenize("..."), Heard(("thỏ", 0, 1)), 0.5));
    }

    [Fact]
    public void Output_IsMonotonicAndHasOneEntryPerSourceWord()
    {
        var tokens = SsmlTokenizer.Tokenize("a b c d e f");
        var json = WordTimingAligner.BuildWordTimingsJson(tokens,
            Heard(("a", 0.0, 0.2), ("c", 0.5, 0.7), ("e", 1.0, 1.2), ("f", 1.2, 1.4)), 0.6);

        var timings = Parse(json!);
        Assert.Equal(6, timings.Count);
        for (var i = 0; i < timings.Count; i++)
        {
            Assert.True(timings[i].End >= timings[i].Start);
            if (i > 0) Assert.True(timings[i].Start >= timings[i - 1].End - 1e-9);
        }
    }
}
