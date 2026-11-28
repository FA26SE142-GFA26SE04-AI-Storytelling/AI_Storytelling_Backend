using StoryPlatform.Application.Features.ListeningInteractions.Services;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class VietnameseLetterUnitsTests
{
    [Theory]
    [InlineData("Nhím", "Nh|í|m")]
    [InlineData("Thỏ", "Th|ỏ")]
    [InlineData("Nghé", "Ngh|é")]
    [InlineData("Gấu", "G|ấ|u")]
    [InlineData("Quạ", "Qu|ạ")]
    [InlineData("Gió", "Gi|ó")]
    [InlineData("Gì", "G|ì")]
    [InlineData("Bống", "B|ố|ng")]
    public void Split_KeepsDigraphsAsOneUnit(string word, string expected) =>
        Assert.Equal(expected, string.Join("|", VietnameseLetterUnits.Split(word)));

    [Theory]
    [InlineData("Nh", true)]
    [InlineData("m", true)]
    [InlineData("a", true)]
    [InlineData("í", false)]
    [InlineData("ấ", false)]
    [InlineData("đ", false)]
    [InlineData(" ", false)]
    public void IsBlankable_OnlyBaseLetters(string unit, bool expected) =>
        Assert.Equal(expected, VietnameseLetterUnits.IsBlankable(unit));

    [Theory]
    [InlineData("b", "bờ")]
    [InlineData("Th", "thờ")]
    [InlineData("ngh", "ngờ")]
    [InlineData("a", "a")]
    public void SoundOf_ReturnsFixedSound(string unit, string expected) =>
        Assert.Equal(expected, VietnameseLetterUnits.SoundOf(unit));

    [Fact]
    public void Split_NormalizesCombiningMarks() =>
        Assert.Equal(VietnameseLetterUnits.Split("Nhím"), VietnameseLetterUnits.Split("Nhím".Normalize(System.Text.NormalizationForm.FormD)));
}
