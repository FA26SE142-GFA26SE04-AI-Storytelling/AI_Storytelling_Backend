using Microsoft.EntityFrameworkCore;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Infrastructure.Persistence.Configurations;
using Xunit;

namespace StoryPlatform.UnitTests.Features.StoryWishes;

public class StoryWishSuggestionModelTests
{
    [Fact]
    public void CardMatch_IsOneRowPerStoryAndCard_WithStringSource()
    {
        var model = new ModelBuilder();
        new StoryWishCardMatchConfiguration().Configure(model.Entity<StoryWishCardMatch>());
        var entity = model.Model.FindEntityType(typeof(StoryWishCardMatch))!;

        Assert.Equal("story_wish_card_matches", entity.GetTableName());
        Assert.Equal(typeof(string), entity.FindProperty(nameof(StoryWishCardMatch.Source))!.GetProviderClrType());
        var unique = Assert.Single(entity.GetIndexes(), index => index.IsUnique);
        Assert.Equal(new[] { nameof(StoryWishCardMatch.StoryId), nameof(StoryWishCardMatch.WishCardId) },
            unique.Properties.Select(p => p.Name));
    }

    [Fact]
    public void Suggestion_HasUniqueRankAndUniqueStoryPerWish()
    {
        var model = new ModelBuilder();
        new StoryWishSuggestionConfiguration().Configure(model.Entity<StoryWishSuggestion>());
        var entity = model.Model.FindEntityType(typeof(StoryWishSuggestion))!;

        Assert.Equal("story_wish_suggestions", entity.GetTableName());
        var uniques = entity.GetIndexes().Where(index => index.IsUnique)
            .Select(index => string.Join(",", index.Properties.Select(p => p.Name))).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "StoryWishId,Rank", "StoryWishId,StoryId" }, uniques);
        Assert.Equal(100, entity.FindProperty(nameof(StoryWishSuggestion.MatchedCardIds))!.GetMaxLength());
        Assert.True(entity.FindProperty(nameof(StoryWishSuggestion.CommunitySubmissionId))!.IsNullable);
    }
}
