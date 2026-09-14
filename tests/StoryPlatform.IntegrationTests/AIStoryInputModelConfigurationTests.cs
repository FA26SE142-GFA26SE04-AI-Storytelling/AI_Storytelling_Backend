using Microsoft.EntityFrameworkCore;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.IntegrationTests;

public sealed class AIStoryInputModelConfigurationTests
{
    [Fact]
    public void Model_has_phase_one_persistence_constraints()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only")
            .Options;
        using var context = new ApplicationDbContext(options);

        var story = context.Model.FindEntityType(typeof(Story))!;
        Assert.True(story.FindProperty(nameof(Story.Title))!.IsNullable);
        Assert.Equal(20, story.FindProperty(nameof(Story.VocabularyLevel))!.GetMaxLength());

        var request = context.Model.FindEntityType(typeof(StoryGenerationRequest))!;
        Assert.Equal("story_generation_requests", request.GetTableName());

        Assert.True(request.FindProperty(nameof(StoryGenerationRequest.ConcurrencyToken))!.IsConcurrencyToken);

        Assert.Contains(request.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(StoryGenerationRequest.SubmittedByUserId), nameof(StoryGenerationRequest.IdempotencyKey)]));
        Assert.Contains(request.GetIndexes(), index => index.IsUnique && index.GetFilter() is not null);
    }

    [Fact]
    public void Model_has_phase_two_current_version_and_active_job_constraints()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only")
            .Options;
        using var context = new ApplicationDbContext(options);

        var version = context.Model.FindEntityType(typeof(StoryVersion))!;
        Assert.Contains(version.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([nameof(StoryVersion.StoryId)]) &&
            index.GetFilter()!.Contains(nameof(StoryVersion.IsCurrent)));

        var job = context.Model.FindEntityType(typeof(StoryGenerationJob))!;
        Assert.True(job.FindProperty(nameof(StoryGenerationJob.ConcurrencyToken))!.IsConcurrencyToken);
        Assert.True(job.FindProperty(nameof(StoryGenerationJob.LeaseExpiresAt))!.IsNullable);
        Assert.Contains(job.GetIndexes(), index => index.IsUnique && index.GetFilter()!.Contains("Processing"));
        Assert.Contains(job.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(StoryGenerationJob.Status),
                nameof(StoryGenerationJob.Stage),
                nameof(StoryGenerationJob.LeaseExpiresAt)]));
    }
}
