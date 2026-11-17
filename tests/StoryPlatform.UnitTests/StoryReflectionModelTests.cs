using Microsoft.EntityFrameworkCore;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class StoryReflectionModelTests
{
    private static ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);

    [Fact]
    public void Reflection_maps_to_its_table_with_one_live_row_per_reading_session()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(StoryReflection))!;

        Assert.Equal("story_reflections", entity.GetTableName());
        var index = Assert.Single(entity.GetIndexes(), x => x.IsUnique);
        Assert.Equal(new[] { "ReadingSessionId" }, index.Properties.Select(p => p.Name));
        Assert.Equal("\"IsDeleted\" = false", index.GetFilter());
        Assert.Equal("text[]", entity.FindProperty(nameof(StoryReflection.Emotions))!.GetColumnType());
        Assert.Equal(200, entity.FindProperty(nameof(StoryReflection.NoteText))!.GetMaxLength());
        Assert.Equal(500, entity.FindProperty(nameof(StoryReflection.NoteAudioStoragePath))!.GetMaxLength());
    }

    [Theory]
    [InlineData("happy", StoryEmotion.Happy, "Vui")]
    [InlineData(" Scared ", StoryEmotion.Scared, "Hơi sợ")]
    [InlineData("EXCITED", StoryEmotion.Excited, "Hồi hộp")]
    public void Emotion_labels_parse_names_case_insensitively(string text, StoryEmotion expected, string label)
    {
        Assert.True(StoryEmotionLabels.TryParse(text, out var emotion));
        Assert.Equal(expected, emotion);
        Assert.Equal(label, StoryEmotionLabels.Label(emotion));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("99")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("Happy, Funny")]
    [InlineData("Angry")]
    public void Emotion_labels_reject_numbers_and_unknown_names(string? text)
    {
        Assert.False(StoryEmotionLabels.TryParse(text, out _));
    }
}
