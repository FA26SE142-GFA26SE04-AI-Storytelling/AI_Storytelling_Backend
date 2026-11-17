using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class ReadAloudRecordingModelTests
{
    private static ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);

    [Fact]
    public void Recording_maps_to_its_table_with_one_live_row_per_target()
    {
        using var db = CreateContext();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ReadAloudRecording))!;

        Assert.Equal("read_aloud_recordings", entity.GetTableName());
        Assert.Equal(500, entity.FindProperty(nameof(ReadAloudRecording.AudioStoragePath))!.GetMaxLength());
        var unique = entity.GetIndexes().Where(x => x.IsUnique).ToArray();
        Assert.Equal(2, unique.Length);
        var segment = Assert.Single(unique, x => x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "ReadingSessionId", "StorySegmentId" }));
        Assert.Equal("\"StorySegmentId\" IS NOT NULL AND \"IsDeleted\" = false", segment.GetFilter());
        var practice = Assert.Single(unique, x => x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "ReadingSessionId", "ListeningInteractionId" }));
        Assert.Equal("\"ListeningInteractionId\" IS NOT NULL AND \"IsDeleted\" = false", practice.GetFilter());
        Assert.Contains(entity.GetCheckConstraints(), c => c.Name == "CK_read_aloud_recordings_one_target");
    }
}
