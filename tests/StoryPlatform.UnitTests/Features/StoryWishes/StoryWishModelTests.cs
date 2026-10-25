using Microsoft.EntityFrameworkCore;
using StoryPlatform.Application.Features.StoryWishes;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence.Configurations;
using Xunit;

namespace StoryPlatform.UnitTests.Features.StoryWishes;

public class StoryWishModelTests
{
    [Fact]
    public void StoryWish_MapsToStoryWishesTable_WithStringEnums()
    {
        var model = new ModelBuilder();
        new StoryWishConfiguration().Configure(model.Entity<StoryWish>());
        var entity = model.Model.FindEntityType(typeof(StoryWish))!;

        Assert.Equal("story_wishes", entity.GetTableName());
        Assert.Equal(typeof(string), entity.FindProperty(nameof(StoryWish.Status))!.GetProviderClrType());
        Assert.Equal(typeof(string), entity.FindProperty(nameof(StoryWish.Kind))!.GetProviderClrType());
        Assert.True(entity.FindProperty(nameof(StoryWish.CharacterCardId))!.IsNullable);
    }

    [Fact]
    public void StoryWish_AllowsAtMostOneLiveDraftPerChild()
    {
        var model = new ModelBuilder();
        new StoryWishConfiguration().Configure(model.Entity<StoryWish>());
        var entity = model.Model.FindEntityType(typeof(StoryWish))!;

        var draftIndex = Assert.Single(entity.GetIndexes(), index => index.IsUnique);
        Assert.Equal(new[] { nameof(StoryWish.ChildProfileId) }, draftIndex.Properties.Select(p => p.Name));
        Assert.Equal("\"Status\" = 'Draft' AND \"IsDeleted\" = false", draftIndex.GetFilter());
    }

    [Fact]
    public void Policy_UsesTheDocumentedLimits()
    {
        Assert.Equal(2, StoryWishPolicy.MaxWaiting);
        Assert.Equal(1, StoryWishPolicy.MaxPerDay);
        Assert.Equal(TimeSpan.FromHours(24), StoryWishPolicy.DraftLifetime);
    }

    [Theory]
    [InlineData("2026-10-22T16:59:00Z", "2026-10-21T17:00:00Z")]
    [InlineData("2026-10-22T17:00:00Z", "2026-10-22T17:00:00Z")]
    [InlineData("2026-10-22T03:00:00Z", "2026-10-21T17:00:00Z")]
    public void StartOfLocalDayUtc_FollowsVietnamMidnight(string nowUtc, string expectedUtc)
    {
        var now = DateTime.Parse(nowUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var expected = DateTime.Parse(expectedUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var start = StoryWishPolicy.StartOfLocalDayUtc(now);
        Assert.Equal(expected, start);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
    }

    [Theory]
    [InlineData(StoryWishStatus.Pending, false, "Bố mẹ đang xem")]
    [InlineData(StoryWishStatus.Accepted, false, "Bố mẹ đang xem")]
    [InlineData(StoryWishStatus.Fulfilled, false, "Điều ước của em đã thành truyện!")]
    [InlineData(StoryWishStatus.Fulfilled, true, "Bố mẹ đã tìm được truyện cho điều ước của em!")]
    [InlineData(StoryWishStatus.Declined, false, "Lần này bố mẹ sẽ kể cho em truyện khác nhé")]
    [InlineData(StoryWishStatus.Expired, false, "Lần này bố mẹ sẽ kể cho em truyện khác nhé")]
    public void StatusMessage_UsesTheDocumentedChildSentences(StoryWishStatus status, bool existing, string expected)
    {
        var wish = new StoryWish { Status = status, FulfilledWithExistingStory = existing };
        Assert.Equal(expected, StoryWishPolicy.StatusMessage(wish));
    }

    [Fact]
    public void StoryWish_MapsDecisionColumns()
    {
        var model = new ModelBuilder();
        new StoryWishConfiguration().Configure(model.Entity<StoryWish>());
        var entity = model.Model.FindEntityType(typeof(StoryWish))!;

        Assert.Equal(500, entity.FindProperty(nameof(StoryWish.DeclineReason))!.GetMaxLength());
        Assert.Equal(50, entity.FindProperty(nameof(StoryWish.ResolutionNote))!.GetMaxLength());
        Assert.True(entity.FindProperty(nameof(StoryWish.GeneratedStoryId))!.IsNullable);
        Assert.Equal(typeof(DateTime?), typeof(StoryWish).GetProperty(nameof(StoryWish.ExpiryWarnedAt))!.PropertyType);
        Assert.Contains(entity.GetForeignKeys(), fk => fk.Properties.Single().Name == nameof(StoryWish.GeneratedStoryId));
        Assert.Contains(entity.GetIndexes(), index =>
            index.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(StoryWish.Status), nameof(StoryWish.PendingSince) }));
    }

    [Fact]
    public void DecisionPolicy_UsesTheDocumentedDeadlines()
    {
        Assert.Equal(TimeSpan.FromDays(7), StoryWishDecisionPolicy.PendingLifetime);
        Assert.Equal(TimeSpan.FromDays(6), StoryWishDecisionPolicy.ExpiryWarningAfter);
        Assert.Equal(TimeSpan.FromDays(7), StoryWishDecisionPolicy.RejectedStoryGrace);
    }

    [Theory]
    [InlineData("Chú mèo", "Khu rừng", "Bị lạc đường", "Chú mèo ở Khu rừng: Bị lạc đường")]
    [InlineData("Chú mèo", "Khu rừng", null, "Chú mèo ở Khu rừng")]
    public void DecisionPolicy_BuildsTheTopicFromTheCards(string character, string place, string? challenge, string expected)
    {
        Assert.Equal(expected, StoryWishDecisionPolicy.Topic(character, place, challenge));
    }

    [Fact]
    public void DecisionPolicy_TopicNeverExceeds200Characters()
    {
        var topic = StoryWishDecisionPolicy.Topic(new string('a', 150), new string('b', 150), null);

        Assert.Equal(200, topic.Length);
    }

    [Fact]
    public void DecisionPolicy_IdempotencyKeyIsStablePerWish_AndWithinTheGenerationLimits()
    {
        var key = StoryWishDecisionPolicy.GenerationKey(42);

        Assert.Equal("story-wish-000042", key);
        Assert.InRange(key.Length, 8, 100);
    }
}
