using Microsoft.EntityFrameworkCore;
using StoryPlatform.Application.Features.WishCards;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence.Configurations;
using Xunit;

namespace StoryPlatform.UnitTests.Features.WishCards;

public class WishCardModelTests
{
    [Fact]
    public void WishCard_MapsToWishCardsTable_WithStringEnumsAndLimits()
    {
        var model = new ModelBuilder();
        new WishCardConfiguration().Configure(model.Entity<WishCard>());
        var entity = model.Model.FindEntityType(typeof(WishCard))!;

        Assert.Equal("wish_cards", entity.GetTableName());
        Assert.Equal(typeof(string), entity.FindProperty(nameof(WishCard.Group))!.GetProviderClrType());
        var age = entity.FindProperty(nameof(WishCard.AgeBand))!;
        Assert.True(age.IsNullable);
        Assert.Equal(typeof(string), age.GetProviderClrType());
        Assert.Equal(100, entity.FindProperty(nameof(WishCard.Label))!.GetMaxLength());
        Assert.False(entity.FindProperty(nameof(WishCard.Label))!.IsNullable);
        Assert.Equal(500, entity.FindProperty(nameof(WishCard.ImageUrl))!.GetMaxLength());
        Assert.Equal(500, entity.FindProperty(nameof(WishCard.AudioUrl))!.GetMaxLength());
        Assert.True(entity.FindProperty(nameof(WishCard.AudioUrl))!.IsNullable);
    }

    [Theory]
    [InlineData("Character", WishCardGroup.Character)]
    [InlineData("character", WishCardGroup.Character)]
    [InlineData(" Place ", WishCardGroup.Place)]
    [InlineData("CHALLENGE", WishCardGroup.Challenge)]
    [InlineData("Lesson", WishCardGroup.Lesson)]
    [InlineData("Desire", WishCardGroup.Desire)]
    [InlineData("Event", WishCardGroup.Event)]
    [InlineData("Ending", WishCardGroup.Ending)]
    public void TryParseGroup_AcceptsTheSevenNamesIgnoringCase(string text, WishCardGroup expected)
    {
        Assert.True(WishCardRules.TryParseGroup(text, out var group));
        Assert.Equal(expected, group);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Animal")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("99")]
    public void TryParseGroup_RejectsUnknownTextAndNumbers(string? text)
    {
        Assert.False(WishCardRules.TryParseGroup(text, out _));
    }

    [Theory]
    [InlineData(WishCardGroup.Character, true)]
    [InlineData(WishCardGroup.Place, true)]
    [InlineData(WishCardGroup.Challenge, true)]
    [InlineData(WishCardGroup.Lesson, true)]
    [InlineData(WishCardGroup.Desire, false)]
    [InlineData(WishCardGroup.Event, false)]
    [InlineData(WishCardGroup.Ending, false)]
    public void IsWishGroup_IsTrueOnlyForTheFourWishGroups(WishCardGroup group, bool expected)
    {
        Assert.Equal(expected, WishCardRules.IsWishGroup(group));
    }

    [Theory]
    [InlineData("https://cdn.example.com/a.png", true)]
    [InlineData("http://cdn.example.com/a.png", true)]
    [InlineData("ftp://cdn.example.com/a.png", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/relative/a.png", false)]
    [InlineData("khong-phai-url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidUrl_AcceptsOnlyAbsoluteHttpUrls(string? value, bool expected)
    {
        Assert.Equal(expected, WishCardRules.IsValidUrl(value));
    }

    [Fact]
    public void IsValidUrl_RejectsUrlsLongerThanTheColumn()
    {
        Assert.False(WishCardRules.IsValidUrl("https://cdn.example.com/" + new string('a', 500)));
    }
}
