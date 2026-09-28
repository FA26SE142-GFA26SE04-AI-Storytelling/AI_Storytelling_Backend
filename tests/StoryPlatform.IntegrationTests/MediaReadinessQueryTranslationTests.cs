using Microsoft.EntityFrameworkCore;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.IntegrationTests;

public sealed class MediaReadinessQueryTranslationTests
{
    [Fact]
    public void Scene_id_filter_translates_for_beats_and_segments_without_database_access()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only")
            .Options;
        using var context = new ApplicationDbContext(options);
        var sceneIds = new List<int> { 1, 2 };

        var beatsSql = context.IllustrationBeats
            .Where(beat => sceneIds.Contains(beat.StorySceneId)).ToQueryString();
        var segmentsSql = context.StorySegments
            .Where(segment => sceneIds.Contains(segment.StorySceneId)).ToQueryString();

        Assert.Contains("illustration_beats", beatsSql);
        Assert.Contains("story_segments", segmentsSql);
    }
}
