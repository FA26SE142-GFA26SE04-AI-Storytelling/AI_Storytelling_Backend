using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

/// <summary>SQLite verifies relational uniqueness, optimistic fencing and rollback, not PostgreSQL advisory locks.</summary>
public sealed class LearningBatchPersistenceTests
{
    [Fact]
    public async Task Duplicate_batch_is_rejected_and_stale_worker_cannot_overwrite_completed_result()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var first = new CreatorManualMediaPersistenceTests.Db(options);
        await first.Database.EnsureCreatedAsync();
        await Seed(first);
        first.LearningArtifactGenerationBatches.Add(Batch());
        await first.SaveChangesAsync();
        first.ChangeTracker.Clear();
        await using var other = new CreatorManualMediaPersistenceTests.Db(options);
        var stale = await other.LearningArtifactGenerationBatches.SingleAsync();
        var current = await first.LearningArtifactGenerationBatches.SingleAsync();
        current.ConcurrencyToken = "new-worker";
        current.Status = GenerationJobStatus.Completed;
        current.ResultJson = "{\"items\":[]}";
        await first.SaveChangesAsync();
        stale.ResultJson = "{\"wrong\":true}";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
        other.ChangeTracker.Clear();
        other.LearningArtifactGenerationBatches.Add(Batch());
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
        Assert.Equal("{\"items\":[]}", await first.LearningArtifactGenerationBatches.AsNoTracking().Select(x => x.ResultJson).SingleAsync());
    }

    [Fact]
    public async Task Rolled_back_claim_does_not_lose_previously_completed_batch()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new CreatorManualMediaPersistenceTests.Db(options);
        await db.Database.EnsureCreatedAsync();
        await Seed(db);
        var completed = Batch(); completed.Status = GenerationJobStatus.Completed; completed.ResultJson = "{}";
        db.LearningArtifactGenerationBatches.Add(completed);
        await db.SaveChangesAsync();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var pending = Batch(); pending.Kind = "quiz_facts";
            db.LearningArtifactGenerationBatches.Add(pending);
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        var saved = await db.LearningArtifactGenerationBatches.SingleAsync();
        Assert.Equal(GenerationJobStatus.Completed, saved.Status);
        Assert.Equal("{}", saved.ResultJson);
    }

    private static LearningArtifactGenerationBatch Batch() => new()
    {
        StoryId = 1, StoryVersionId = 10, RootJobId = 1, RequestedByUserId = 1,
        Kind = "vocabulary", TargetCount = 12, RequestJson = "{}", SourceFingerprint = new('a', 64), ContextFingerprint = new('b', 64)
    };
    private static async Task Seed(ApplicationDbContext db)
    {
        db.Users.Add(new() { Id = 1, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        db.Stories.Add(new() { Id = 1, AuthorUserId = 1 });
        db.StoryVersions.Add(new() { Id = 10, StoryId = 1, IsCurrent = true });
        db.StoryGenerationJobs.Add(new() { Id = 1, StoryId = 1, StoryVersionId = 10, RequestedByUserId = 1 });
        await db.SaveChangesAsync();
    }
}
