using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Moq;
using StoryPlatform.Application.Features.ContentGeneration.Services;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

/// <summary>SQLite verifies relational uniqueness, optimistic fencing and rollback, not PostgreSQL advisory locks.</summary>
public sealed class LearningBatchPersistenceTests
{
    [Theory]
    [InlineData(GenerationJobStatus.Pending)]
    [InlineData(GenerationJobStatus.Processing)]
    [InlineData(GenerationJobStatus.Failed)]
    public async Task Legacy_discussion_does_not_block_or_capture_quiz_retry(GenerationJobStatus legacyStatus)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        // Exercise relational queries and transactions; PostgreSQL advisory locking is not under test.
        connection.CreateFunction<long, int>("pg_advisory_xact_lock", _ => 0);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new CreatorManualMediaPersistenceTests.Db(options);
        await db.Database.EnsureCreatedAsync();
        await Seed(db);
        var story = await db.Stories.SingleAsync();
        story.Scope = StoryScope.Creator;
        story.Source = StorySource.Ai;
        (await db.StoryVersions.SingleAsync()).Content = "Lan chia sẻ sách với Minh.";
        var root = await db.StoryGenerationJobs.SingleAsync();
        root.Operation = GenerationJobOperation.GenerateVocabulary;
        root.Status = GenerationJobStatus.Completed;
        db.StoryGenerationJobs.Add(new()
        {
            Id = 2, StoryId = 1, StoryVersionId = 10, RequestedByUserId = 1,
            Operation = GenerationJobOperation.GenerateQuiz, Status = GenerationJobStatus.Failed,
            ErrorCode = "QUIZ_GENERATION_FAILED", CompletedAt = DateTime.UtcNow.AddMinutes(-1)
        });
        db.StoryGenerationJobs.Add(new()
        {
            Id = 3, StoryId = 1, StoryVersionId = 10, RequestedByUserId = 1,
            Operation = GenerationJobOperation.GenerateDiscussion, Status = legacyStatus,
            ErrorCode = "DISCUSSION_GENERATION_FAILED", CompletedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new ContentGenerationService(new UnitOfWork(db), null!, null!, null!, null!, new(),
            learningBatches: new Mock<ILearningBatchService>().Object);

        // This reduced relational model excludes QuizItem, which the public progress DTO loads after retry.
        // Exercise the actual transactional retry operation without that unrelated projection.
        var retry = typeof(ContentGenerationService).GetMethod("RetryLearningArtifactsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.True(await (Task<bool>)retry.Invoke(service,
            [1, 1, "retry-quiz-ignore-legacy-discussion", CancellationToken.None])!);

        var jobs = await db.StoryGenerationJobs.AsNoTracking().ToListAsync();
        Assert.Equal(GenerationJobStatus.Pending, jobs.Single(x => x.Id == 2).Status);
        Assert.Null(jobs.Single(x => x.Id == 2).ErrorCode);
        Assert.Equal(legacyStatus, jobs.Single(x => x.Id == 3).Status);
        Assert.Equal("DISCUSSION_GENERATION_FAILED", jobs.Single(x => x.Id == 3).ErrorCode);
    }

    [Fact]
    public async Task Coordinator_requeues_detached_failed_quiz_without_losing_original_concurrency_token()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db=new CreatorManualMediaPersistenceTests.Db(options);
        await db.Database.EnsureCreatedAsync();await Seed(db);
        var source=await db.StoryGenerationJobs.SingleAsync();source.Operation=GenerationJobOperation.GenerateVocabulary;
        db.StoryGenerationJobs.Add(new() {Id=2,StoryId=1,StoryVersionId=10,RequestedByUserId=1,Operation=GenerationJobOperation.GenerateQuiz,
            Status=GenerationJobStatus.Failed,ErrorCode="QUIZ_VALIDATION_FAILED",ConcurrencyToken="original-quiz-token"});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var service=new ContentGenerationService(new UnitOfWork(db),null!,null!,null!,null!,new(),learningBatches:new Mock<ILearningBatchService>().Object);
        var handoff=typeof(ContentGenerationService).GetMethod("EnsureNextJobAsync",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var nextType = handoff.GetParameters()[2].ParameterType;
        var next = Activator.CreateInstance(nextType, [GenerationJobOperation.GenerateQuiz, null])!;
        await (Task)handoff.Invoke(service, [source, 10, next, CancellationToken.None])!;
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var quiz=await db.StoryGenerationJobs.AsNoTracking().SingleAsync(x=>x.Id==2);
        Assert.Equal(GenerationJobStatus.Pending,quiz.Status);Assert.Null(quiz.ErrorCode);
        Assert.NotEqual("original-quiz-token",quiz.ConcurrencyToken);
    }
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
