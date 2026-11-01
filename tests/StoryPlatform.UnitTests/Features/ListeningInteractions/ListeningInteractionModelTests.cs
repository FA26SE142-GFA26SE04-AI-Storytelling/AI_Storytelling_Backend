using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningInteractionModelTests
{
    private static ApplicationDbContext MetadataOnly() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);

    private static IIndex Index(IEntityType entity, params string[] columns) =>
        entity.GetIndexes().Single(i => i.Properties.Select(p => p.Name).SequenceEqual(columns));

    [Fact]
    public void Interaction_HasOneRowPerAnchorPerVersion()
    {
        using var db = MetadataOnly();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ListeningInteraction))!;
        Assert.Equal("listening_interactions", entity.GetTableName());
        var index = Index(entity, nameof(ListeningInteraction.StoryVersionId), nameof(ListeningInteraction.AnchorOffset));
        Assert.True(index.IsUnique);
        Assert.Equal("\"IsDeleted\" = false", index.GetFilter());
        Assert.Equal("Checkpoint", entity.FindProperty(nameof(ListeningInteraction.Type))!.GetTypeMapping().Converter!
            .ConvertToProvider(StoryPlatform.Domain.Enums.ListeningInteractionType.Checkpoint));
    }

    [Fact]
    public void Option_SortOrderIsUniquePerInteraction()
    {
        using var db = MetadataOnly();
        var entity = db.Model.FindEntityType(typeof(ListeningInteractionOption))!;
        Assert.Equal("listening_interaction_options", entity.GetTableName());
        var index = Index(entity, nameof(ListeningInteractionOption.ListeningInteractionId), nameof(ListeningInteractionOption.SortOrder));
        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Attempt_IsIndexedBySessionAndInteraction()
    {
        using var db = MetadataOnly();
        var entity = db.Model.FindEntityType(typeof(ListeningInteractionAttempt))!;
        Assert.Equal("listening_interaction_attempts", entity.GetTableName());
        var index = Index(entity, nameof(ListeningInteractionAttempt.ReadingSessionId), nameof(ListeningInteractionAttempt.ListeningInteractionId));
        Assert.False(index.IsUnique);
    }

    [Fact]
    public void MediaAsset_RequiresInteractionForListeningMedia()
    {
        using var db = MetadataOnly();
        var names = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(MediaAsset))!.GetCheckConstraints().Select(c => c.Name).ToArray();
        Assert.Contains("CK_media_assets_interaction_required_for_listening_audio", names);
        Assert.Contains("CK_media_assets_option_required_for_listening_image", names);
    }

    [Fact]
    public void Job_AllowsOneListeningJobPerTypePerVersion()
    {
        using var db = MetadataOnly();
        var entity = db.Model.FindEntityType(typeof(StoryGenerationJob))!;
        var legacy = Index(entity, nameof(StoryGenerationJob.Operation), nameof(StoryGenerationJob.StoryVersionId));
        Assert.Contains("\"Operation\" <> 'GenerateListeningInteraction'", legacy.GetFilter());
        var perType = Index(entity, nameof(StoryGenerationJob.StoryVersionId), nameof(StoryGenerationJob.ListeningInteractionType));
        Assert.True(perType.IsUnique);
        Assert.Contains("\"Operation\" = 'GenerateListeningInteraction'", perType.GetFilter());
        // Index FK thường vẫn phải còn; index có filter ở trên không thay được nó.
        Assert.Null(Index(entity, nameof(StoryGenerationJob.StoryVersionId)).GetFilter());
    }
}
