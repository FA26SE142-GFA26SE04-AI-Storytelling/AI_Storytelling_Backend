using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.ContentGeneration;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration.Services;
using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Configurations;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningWorkflowPersistenceTests
{
    [Fact]
    public async Task Lease_renewal_uses_database_token_and_does_not_revive_completed_work()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ListeningDb(options);
        await db.Database.EnsureCreatedAsync();
        db.Add(new StoryGenerationJob { Id = 1, StoryId = 1, Status = GenerationJobStatus.Processing,
            ConcurrencyToken = "owner", LeaseExpiresAt = DateTime.UtcNow.AddSeconds(10) });
        await db.SaveChangesAsync();
        using var provider = new ServiceCollection().AddScoped<ApplicationDbContext>(_ => new ListeningDb(options)).BuildServiceProvider();
        var lease = new StoryPlatform.Infrastructure.BackgroundServices.ContentGenerationJobFailureFinalizer(provider.GetRequiredService<IServiceScopeFactory>());
        Assert.False(await lease.RenewAsync(1, "other-owner", TimeSpan.FromMinutes(4), default));
        Assert.True(await lease.RenewAsync(1, "owner", TimeSpan.FromMinutes(4), default));
        Assert.True((await db.StoryGenerationJobs.AsNoTracking().SingleAsync()).LeaseExpiresAt > DateTime.UtcNow.AddMinutes(3));
        await db.StoryGenerationJobs.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, GenerationJobStatus.Completed));
        Assert.False(await lease.RenewAsync(1, "owner", TimeSpan.FromMinutes(4), default));
    }

    [Fact]
    public async Task Phase4_jobs_persist_valid_package_and_snapshots_across_scopes_until_content_review()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction<long, int>("pg_advisory_xact_lock", _ => 0);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using (var seed = new ListeningDb(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Add(new UserAccount { Id = 1, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
            seed.Add(new Story { Id = 1, AuthorUserId = 1, Scope = StoryScope.Creator, Source = StorySource.Ai, Status = StoryStatus.OutlineReview });
            seed.Add(new StoryVersion { Id = 1, StoryId = 1, VersionNo = 1, Title = "Thỏ con", Content = ListeningTestStory.Content,
                Lesson = "Biết nhờ người lớn giúp đỡ", IsCurrent = true });
            var context = new AIStoryInputContextSnapshot(null, "6-8", 2, "level_2", "vi", 1000, "always_manual", [], [], [], [],
                AudienceScope: "Creator", CreatorUserId: 1);
            seed.Add(new StoryGenerationRequest { Id = 1, StoryId = 1, SubmittedByUserId = 1, IdempotencyKey = "fixture",
                Status = GenerationInputStatus.InputAccepted, ContextSnapshotJson = JsonSerializer.Serialize(context, JsonOptions) });
            seed.Add(new StoryVocabulary { Id = 1, StoryVersionId = 1, Term = "Gấu", Definition = "Con vật trong truyện" });
            seed.Add(new QuizItem { Id = 1, StoryVersionId = 1, Type = QuizType.ShortAnswer, Question = "Ai giúp Thỏ?", CorrectAnswer = "Bác Gấu" });
            seed.Add(new StoryGenerationJob { Id = 1, StoryId = 1, StoryVersionId = 1, GenerationRequestId = 1, RequestedByUserId = 1,
                OperationKey = "fixture-listening", Operation = GenerationJobOperation.GenerateListeningInteraction,
                ListeningInteractionType = ListeningInteractionType.Checkpoint, MaxAttempts = 3 });
            await seed.SaveChangesAsync();
        }
        var ai = ListeningTestStory.AiClient();
        var failures = new List<string>();
        var finalizer = new Mock<IContentGenerationJobFailureFinalizer>();
        finalizer.Setup(x => x.MarkFailedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<int, string, string, CancellationToken>((id, token, code, ct) => failures.Add(code)).Returns(Task.CompletedTask);
        for (var i = 0; i < 4; i++)
        {
            await using var db = new ListeningDb(options);
            var service = new ContentGenerationService(new UnitOfWork(db), ai.Object, null!, finalizer.Object, null!, new());
            Assert.True(await service.ProcessNextAsync());
            Assert.Empty(failures);
        }
        await using var result = new ListeningDb(options);
        var jobs = await result.StoryGenerationJobs.AsNoTracking().ToListAsync();
        Assert.Equal(4, jobs.Count);
        Assert.All(jobs, x => Assert.Equal(GenerationJobStatus.Completed, x.Status));
        Assert.All(jobs, x =>
        {
            var receipt = JsonSerializer.Deserialize<ListeningJobState>(x.GenerationMetadataJson!, JsonOptions)!;
            Assert.NotNull(receipt.PinnedRequest?.Snapshot);
            Assert.Equal(1, receipt.ProviderCalls);
        });
        Assert.Equal(5, await result.Set<ListeningInteraction>().CountAsync());
        Assert.Equal(StoryStatus.ContentReview, (await result.Stories.SingleAsync()).Status);
        var progress = await new ContentGenerationService(new UnitOfWork(result), ai.Object, null!, finalizer.Object, null!, new()).GetProgressAsync(1, 1);
        Assert.True(progress.IsComplete);
        Assert.Equal(4, progress.ListeningDetails.Count);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed class ListeningDb(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            Type[] types = [typeof(UserAccount), typeof(Story), typeof(StoryVersion), typeof(StoryGenerationRequest), typeof(StoryGenerationJob),
                typeof(StoryVocabulary), typeof(QuizItem), typeof(ListeningInteraction), typeof(ListeningInteractionOption)];
            foreach (var type in types)
                foreach (var property in type.GetProperties().Where(p => typeof(BaseEntity).IsAssignableFrom(p.PropertyType) ||
                    p.PropertyType.IsGenericType && p.PropertyType.GetGenericArguments().Any(t => typeof(BaseEntity).IsAssignableFrom(t))))
                    builder.Entity(type).Ignore(property.Name);
            foreach (var type in builder.Model.GetEntityTypes().Select(x => x.ClrType).ToArray())
                if (!types.Contains(type)) builder.Ignore(type);
            foreach (var type in types) builder.Entity(type).HasKey("Id");
            builder.Entity<StoryGenerationJob>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            builder.Entity<StoryGenerationJob>().HasIndex(x => x.OperationKey).IsUnique();
            new StoryVersionConfiguration().Configure(builder.Entity<StoryVersion>());
        }
    }
}
