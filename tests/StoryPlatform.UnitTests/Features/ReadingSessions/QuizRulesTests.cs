using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class QuizRulesTests
{
    [Theory]
    [InlineData(QuizType.ShortAnswer, ".", "!")]
    [InlineData(QuizType.ShortAnswer, "...|Sao Hỏa", "?!")]
    [InlineData(QuizType.TrueFalse, "?", "!")]
    [InlineData(QuizType.TrueFalse, "maybe", "maybe")]
    [InlineData((QuizType)999, "a", "a")]
    public void InvalidKeyOrAnswer_IsNeverCorrect(QuizType type, string correct, string given)
    {
        Assert.False(QuizRules.IsCorrect(type, correct, given));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[null,\"  \"]")]
    [InlineData("[1,true]")]
    public void ParseChoices_UnusableJson_ReturnsSafeDefaults(string json)
    {
        Assert.Empty(QuizRules.ParseChoices(QuizType.MultipleChoice, json));
        Assert.Equal(new[] { "Đúng", "Sai" }, QuizRules.ParseChoices(QuizType.TrueFalse, json));
    }

    [Theory]
    [InlineData("Chú Thỏ", "chú thỏ", true)]
    [InlineData("Chú Thỏ", "  Chú   Thỏ ", true)]
    [InlineData("Chú Thỏ", "Chú Rùa", false)]
    [InlineData("Chú Thỏ", "chu tho", false)]
    public void MultipleChoice_MatchesTextIgnoringCaseAndSpaces(string correct, string given, bool expected)
    {
        Assert.Equal(expected, QuizRules.IsCorrect(QuizType.MultipleChoice, correct, given));
    }

    [Theory]
    [InlineData("true", "Đúng", true)]
    [InlineData("Đúng", "true", true)]
    [InlineData("Dung", "đúng", true)]
    [InlineData("false", "Sai", true)]
    [InlineData("Sai", "false", true)]
    [InlineData("Đúng", "Sai", false)]
    [InlineData("true", "false", false)]
    public void TrueFalse_AcceptsBothVocabularies(string correct, string given, bool expected)
    {
        Assert.Equal(expected, QuizRules.IsCorrect(QuizType.TrueFalse, correct, given));
    }

    [Theory]
    [InlineData("Sao Hoa", "sao hoa", true)]
    [InlineData("Sao Hoa", "Sao Hỏa", true)]
    [InlineData("Sao Hoa", "Sao Hoa!", true)]
    [InlineData("Sao Hoa|Hỏa tinh", "hoa tinh", true)]
    [InlineData("Sao Hoa", "Sao Kim", false)]
    public void ShortAnswer_IgnoresCaseAccentsAndPunctuation_AndAllowsAlternatives(
        string correct, string given, bool expected)
    {
        Assert.Equal(expected, QuizRules.IsCorrect(QuizType.ShortAnswer, correct, given));
    }

    [Theory]
    [InlineData(QuizType.MultipleChoice, null, "a")]
    [InlineData(QuizType.MultipleChoice, "a", null)]
    [InlineData(QuizType.ShortAnswer, "a", "   ")]
    [InlineData(QuizType.TrueFalse, "", "Đúng")]
    public void MissingAnswerOrKey_IsNeverCorrect(QuizType type, string? correct, string? given)
    {
        Assert.False(QuizRules.IsCorrect(type, correct, given));
    }

    [Fact]
    public void Fold_RemovesAccentsAndMapsDWithStroke()
    {
        Assert.Equal("dong thap", QuizRules.Fold("Đồng  Tháp."));
    }

    [Fact]
    public void ParseChoices_ReadsJsonArrayAndTrims()
    {
        var choices = QuizRules.ParseChoices(QuizType.MultipleChoice, "[\" Chú Thỏ \",\"Chú Rùa\",\"\"]");

        Assert.Equal(new[] { "Chú Thỏ", "Chú Rùa" }, choices);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("không phải json")]
    public void ParseChoices_TrueFalseWithoutUsableData_FallsBackToDungSai(string? json)
    {
        Assert.Equal(new[] { "Đúng", "Sai" }, QuizRules.ParseChoices(QuizType.TrueFalse, json));
    }

    [Fact]
    public void ParseChoices_BrokenJsonForMultipleChoice_IsEmpty()
    {
        Assert.Empty(QuizRules.ParseChoices(QuizType.MultipleChoice, "[oops"));
    }

    [Fact]
    public void ParseChoices_ShortAnswerNeverHasChoices()
    {
        Assert.Empty(QuizRules.ParseChoices(QuizType.ShortAnswer, "[\"a\",\"b\"]"));
    }
}
