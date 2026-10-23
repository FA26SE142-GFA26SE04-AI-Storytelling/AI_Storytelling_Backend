using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed partial class SplitMediaPersistenceTests
{
    private sealed class GatedImages : IImageGenerationProvider
    {
        public TaskCompletionSource[] Started { get; } = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        public TaskCompletionSource[] Release { get; } = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        private int _calls;
        public async Task<GeneratedMedia> GenerateAsync(SceneSpecification spec, CancellationToken ct = default)
        {
            var index = Interlocked.Increment(ref _calls) - 1;
            Started[index].TrySetResult();
            await Release[index].Task.WaitAsync(ct);
            return new GeneratedMedia([1, 2, 3], "image/png");
        }
    }

    [Fact]
    public async Task SeparateDbContextsClaimDifferentImagesAndRespectTwoPerStoryLimit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db1 = new CreatorManualMediaPersistenceTests.Db(options);
        await using var db2 = new CreatorManualMediaPersistenceTests.Db(options);
        await using var db3 = new CreatorManualMediaPersistenceTests.Db(options);
        await db1.Database.EnsureCreatedAsync();
        await SeedAsync(db1);
        var version = await db1.StoryVersions.SingleAsync();
        version.Content = "A\n\nB\n\nC";
        await db1.SaveChangesAsync();
        db1.ChangeTracker.Clear();
        // SQLite has no advisory locks; serialize short transactions to model the production story lock.
        using var gate = new SemaphoreSlim(1, 1);
        await using var uow1 = new UnitOfWork(db1);
        await using var uow2 = new UnitOfWork(db2);
        await using var uow3 = new UnitOfWork(db3);
        var images = new GatedImages();
        var first = MediaGenerationServiceTests.Create(Proxy(uow1, transactionGate: gate), image: images);
        var second = MediaGenerationServiceTests.Create(Proxy(uow2, transactionGate: gate), image: images);
        var third = MediaGenerationServiceTests.Create(Proxy(uow3, transactionGate: gate), image: images);
        await first.ProcessNextAsync();
        var work1 = first.ProcessBranchNextAsync(MediaType.Illustration);
        await images.Started[0].Task.WaitAsync(TimeSpan.FromSeconds(5));
        var work2 = second.ProcessBranchNextAsync(MediaType.Illustration);
        await images.Started[1].Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.False((await third.ProcessBranchNextAsync(MediaType.Illustration)).JobFound);
            var running = await db3.MediaAssetGenerationTasks.AsNoTracking().Where(x => x.Status == GenerationJobStatus.Processing).ToListAsync();
            Assert.Equal(2, running.Count);
            Assert.Equal(2, running.Select(x => x.MediaAssetId).Distinct().Count());
            Assert.Single(await db3.MediaAssetGenerationTasks.AsNoTracking().Where(x => x.Status == GenerationJobStatus.Pending).ToListAsync());
            images.Release[0].TrySetResult();
            Assert.True((await work1).Success);
            var work3 = third.ProcessBranchNextAsync(MediaType.Illustration);
            await images.Started[2].Task.WaitAsync(TimeSpan.FromSeconds(5));
            images.Release[2].TrySetResult();
            Assert.True((await work3).Success);
            images.Release[1].TrySetResult();
            Assert.True((await work2).Success);
            Assert.True((await first.RunPackageAsync()).Success);
            Assert.Equal(3, await db1.MediaAssets.CountAsync(x => x.Type == MediaType.Illustration && x.Status == MediaStatus.Ready));
            Assert.Equal(3, await db1.MediaAssetGenerationTasks.CountAsync(x => x.Status == GenerationJobStatus.Completed));
        }
        finally { foreach (var release in images.Release) release.TrySetResult(); await Task.WhenAll(work1, work2); }
    }
}
