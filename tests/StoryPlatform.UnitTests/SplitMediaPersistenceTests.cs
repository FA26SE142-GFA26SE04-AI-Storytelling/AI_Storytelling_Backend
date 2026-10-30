using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.UnitTests;

/// <summary>Real SQLite transactions/repositories and filtered indexes; providers and advisory locks are doubles.</summary>
public sealed partial class SplitMediaPersistenceTests
{
    private static async Task SeedAsync(ApplicationDbContext db)
    {
        db.Users.Add(new() { Id = 1, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        db.Stories.Add(new() { Id = 1, AuthorUserId = 1, Scope = StoryScope.Creator, Source = StorySource.Manual, Status = StoryStatus.Approved });
        db.StoryVersions.Add(new() { Id = 10, StoryId = 1, IsCurrent = true, Title = "Test", Content = "A\n\nB" });
        db.StoryGenerationJobs.Add(new() { Id = 1, StoryId = 1, StoryVersionId = 10, RequestedByUserId = 1,
            Operation = GenerationJobOperation.GenerateMediaPackage, Status = GenerationJobStatus.Pending });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private sealed class CallbackImage(Func<Task> callback) : IImageGenerationProvider
    {
        public async Task<GeneratedMedia> GenerateAsync(SceneSpecification specification, CancellationToken cancellationToken = default)
        {
            await callback();
            return new GeneratedMedia([1, 2, 3], "image/png");
        }
    }
    private sealed class Capture : ILogger<MediaGenerationService>
    {
        public List<Exception> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null) Errors.Add(exception);
        }
    }
    private static IUnitOfWork Proxy(UnitOfWork real, Func<bool>? fail = null, SemaphoreSlim? transactionGate = null)
    {
        var mock = new Mock<IUnitOfWork>();
        var held = false;
        void ReleaseGate() { if (held) { held = false; transactionGate!.Release(); } }
        void Register<T>() where T : class => mock.Setup(x => x.Repository<T>()).Returns(real.Repository<T>());
        Register<UserAccount>(); Register<Story>(); Register<StoryVersion>(); Register<StoryGenerationJob>();
        Register<MediaContext>(); Register<StoryScene>(); Register<IllustrationBeat>(); Register<StorySegment>();
        Register<MediaAsset>(); Register<ModerationItem>(); Register<StoryAuthoringOperation>();
        Register<MediaAssetGenerationTask>(); Register<StoryVocabulary>();
        mock.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken ct) =>
        {
            if (transactionGate is not null) { await transactionGate.WaitAsync(ct); held = true; }
            try { await real.BeginTransactionAsync(ct); }
            catch { ReleaseGate(); throw; }
        });
        mock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.SaveChangesAsync(ct));
        mock.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken ct) =>
        {
            await real.SaveChangesAsync(ct);
            if (fail?.Invoke() == true) throw new InvalidOperationException("Injected commit failure");
            await real.CommitTransactionAsync(ct);
            ReleaseGate();
        });
        mock.Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken ct) =>
        {
            try { await real.RollbackTransactionAsync(ct); }
            finally { ReleaseGate(); }
        });
        mock.Setup(x => x.DiscardTrackedChanges()).Callback(real.DiscardTrackedChanges);
        mock.Setup(x => x.AcquireTransactionLockAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return mock.Object;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacementPromotionIsAtomicAndFilteredIndexesRetainExactlyOneSelectedAsset(bool failPromotion)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new CreatorManualMediaPersistenceTests.Db(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await SeedAsync(db);
        await using var real = new UnitOfWork(db);
        var inject = false;
        var uow = Proxy(real, () => inject && failPromotion && db.ChangeTracker.Entries<MediaAsset>().Any(x =>
            x.Entity.ReplacesMediaAssetId.HasValue && x.Entity.IsCurrent));
        var logs = new Capture();
        var service = MediaGenerationServiceTests.Create(uow, logger: logs);
        var initial = await service.RunPackageAsync();
        Assert.True(initial.Success, string.Join("\n", logs.Errors));
        Assert.Equal(3, await db.StoryGenerationJobs.CountAsync());
        var original = await db.MediaAssets.AsNoTracking().FirstAsync(x => x.Type == MediaType.Illustration);
        await service.RegenerateIllustrationBeatAsync(1, 1, original.IllustrationBeatId!.Value, new(10, 1, false, "replace-db"));
        await service.ProcessNextAsync();
        inject = true;
        var branch = await service.ProcessBranchNextAsync(MediaType.Illustration);
        Assert.Equal(!failPromotion, branch.Success);
        db.ChangeTracker.Clear();
        var saved = await db.MediaAssets.AsNoTracking().Where(x => x.IllustrationBeatId == original.IllustrationBeatId).ToListAsync();
        Assert.Equal(2, saved.Count);
        var selected = Assert.Single(saved, x => x.IsCurrent);
        Assert.Equal(failPromotion ? original.Id : saved.Single(x => x.ReplacesMediaAssetId == original.Id).Id, selected.Id);
        Assert.Equal(original.Url, saved.Single(x => x.Id == original.Id).Url);
        Assert.Equal(failPromotion, saved.Single(x => x.ReplacesMediaAssetId.HasValue).ReplacementResolvedAt is null);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProviderResultCannotUseCachedClaimAfterAnotherContextChangesOrExpiresTheLease(bool rotateToken)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new CreatorManualMediaPersistenceTests.Db(options);
        await db.Database.EnsureCreatedAsync();
        await SeedAsync(db);
        await using var real = new UnitOfWork(db);
        var changed = false;
        var image = new CallbackImage(async () =>
        {
            if (changed) return;
            changed = true;
            await using var other = new CreatorManualMediaPersistenceTests.Db(options);
            var task = await other.MediaAssetGenerationTasks.SingleAsync(x => x.Status == GenerationJobStatus.Processing);
            if (rotateToken) task.ConcurrencyToken = "another-worker-claim";
            else task.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await other.SaveChangesAsync();
        });
        var service = MediaGenerationServiceTests.Create(Proxy(real), image: image);
        await service.ProcessNextAsync();
        Assert.False((await service.ProcessBranchNextAsync(MediaType.Illustration)).Success);
        db.ChangeTracker.Clear();
        Assert.False(await db.MediaAssets.AnyAsync(x => x.Status == MediaStatus.Ready));
        if (rotateToken)
            Assert.Contains(await db.MediaAssetGenerationTasks.ToListAsync(), x => x.ConcurrencyToken == "another-worker-claim");
        Assert.Equal(StoryStatus.MediaProcessing, (await db.Stories.SingleAsync()).Status);
        Assert.True((await service.ProcessBranchNextAsync(MediaType.TtsAudio)).Success);
        if (!rotateToken)
        {
            Assert.True((await service.RunPackageAsync()).Success);
            Assert.Equal(StoryStatus.MediaReview, (await db.Stories.AsNoTracking().SingleAsync()).Status);
        }
    }
}
