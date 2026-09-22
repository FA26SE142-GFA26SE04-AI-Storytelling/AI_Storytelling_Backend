using StoryPlatform.Application.Features.MediaGeneration.Services;
using Xunit;

namespace StoryPlatform.UnitTests;

/// <summary>
/// Phase 5 tests for SSML tokenizer behavior: preserves whitespace, punctuation, Vietnamese Unicode
/// grapheme clusters, and original text exactly. Empty-element &lt;mark/&gt; tags are used; timing
/// marks are derived from grapheme-level tokenization to keep alignment deterministic.
/// </summary>
public sealed class SsmlTokenizerTests
{
    [Fact]
    public void Tokenize_EmptyString_ReturnsEmpty()
    {
        Assert.Empty(SsmlTokenizer.Tokenize(""));
        Assert.Empty(SsmlTokenizer.Tokenize(null!));
    }

    [Fact]
    public void Tokenize_SingleWord_ReturnsOneWordToken()
    {
        var tokens = SsmlTokenizer.Tokenize("hello");
        Assert.Single(tokens);
        Assert.Equal("hello", tokens[0].Text);
        Assert.True(tokens[0].IsWord);
        Assert.Equal(0, tokens[0].OriginalStartOffset);
        Assert.Equal(5, tokens[0].OriginalEndOffset);
    }

    [Fact]
    public void Tokenize_WhitespaceOnlyBetweenWords_RoundTripsOriginalText()
    {
        const string input = "Ngày    xưa  có";
        var tokens = SsmlTokenizer.Tokenize(input);
        var reconstructed = string.Concat(tokens.Select(t => t.Text));
        Assert.Equal(input, reconstructed);
        Assert.Contains(tokens, t => t.IsWord && t.Text == "Ngày");
        Assert.Contains(tokens, t => t.IsWord && t.Text == "xưa");
        Assert.Contains(tokens, t => t.IsWord && t.Text == "có");
    }

    [Fact]
    public void Tokenize_VietnameseDiacritics_Preserved()
    {
        var tokens = SsmlTokenizer.Tokenize("ế ữ ơ");
        Assert.Equal(3, tokens.Count(t => t.IsWord));
        Assert.Contains(tokens, t => t.IsWord && t.Text == "ế");
        Assert.Contains(tokens, t => t.IsWord && t.Text == "ữ");
        Assert.Contains(tokens, t => t.IsWord && t.Text == "ơ");
    }

    [Fact]
    public void Tokenize_PunctuationAsSeparateToken()
    {
        var tokens = SsmlTokenizer.Tokenize("Hello!");
        Assert.Equal(2, tokens.Count);
        Assert.True(tokens[0].IsWord);
        Assert.Equal("Hello", tokens[0].Text);
        Assert.False(tokens[1].IsWord);
        Assert.Equal("!", tokens[1].Text);
    }

    [Fact]
    public void BuildSsmlWithMarks_InjectsEmptyMarksOnlyBeforeWords()
    {
        var tokens = SsmlTokenizer.Tokenize("hi . there");
        var ssml = SsmlTokenizer.BuildSsmlWithMarks(tokens);
        // Two <mark> tags for two words, no trailing mark for "."
        Assert.Contains("<mark name=\"w0\"/>hi", ssml);
        Assert.Contains("<mark name=\"w4\"/>there", ssml);
        // Punctuation "." must NOT be immediately preceded by a <mark>
        Assert.Contains(". ", ssml);
        // Count <mark> tags — must equal number of word tokens (2)
        var markCount = System.Text.RegularExpressions.Regex.Matches(ssml, "<mark name=").Count;
        var wordTokenCount = tokens.Count(t => t.IsWord);
        Assert.Equal(wordTokenCount, markCount);
    }

    [Fact]
    public void EscapeXml_HandlesSpecialCharacters()
    {
        Assert.Equal("&amp;hello", SsmlTokenizer.EscapeXml("&hello"));
        Assert.Equal("&lt;tag&gt;", SsmlTokenizer.EscapeXml("<tag>"));
        Assert.Equal("a&quot;b", SsmlTokenizer.EscapeXml("a\"b"));
    }

    [Fact]
    public void Tokenize_RoundTripWithPunctuation()
    {
        const string input = "Xin chào, bạn!";
        var tokens = SsmlTokenizer.Tokenize(input);
        var reconstructed = string.Concat(tokens.Select(t => t.Text));
        Assert.Equal(input, reconstructed);
    }
}
