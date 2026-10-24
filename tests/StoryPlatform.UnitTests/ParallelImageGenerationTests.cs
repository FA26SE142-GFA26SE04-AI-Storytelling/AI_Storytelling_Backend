using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed partial class MediaGenerationServiceTests
{
    private sealed class BlockingImages : IImageGenerationProvider
    {
        private readonly TaskCompletionSource[] _started = Enumerable.Range(0, 8).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        private readonly TaskCompletionSource[] _release = Enumerable.Range(0, 8).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        private int _calls;
        public int Calls => _calls;
        public Task Started(int index) => _started[index].Task.WaitAsync(TimeSpan.FromSeconds(5));
        public void Release(int index) => _release[index].TrySetResult();
        public void ReleaseAll() { foreach (var gate in _release) gate.TrySetResult(); }
        public async Task<GeneratedMedia> GenerateAsync(SceneSpecification specification, CancellationToken cancellationToken = default)
        {
            var index = Interlocked.Increment(ref _calls) - 1;
            _started[index].TrySetResult();
            await _release[index].Task.WaitAsync(cancellationToken);
            return new GeneratedMedia([1, 2, 3], "image/png");
        }
    }

    [Fact]
    public async Task IndependentProcessorsRunTwoImagesAndLeaveThirdWaitingUntilOneCompletes()
    {
        var store = Seed();
        store.Items<StoryVersion>().Single().Content = "A\n\nB\n\nC";
        var provider = new BlockingImages();
        var first = Create(store, image: provider);
        var second = Create(store, image: provider);
        var third = Create(store, image: provider);
        await first.ProcessNextAsync();
        var task1 = first.ProcessBranchNextAsync(MediaType.Illustration);
        await provider.Started(0);
        var task2 = second.ProcessBranchNextAsync(MediaType.Illustration);
        await provider.Started(1);
        try
        {
            Assert.False((await third.ProcessBranchNextAsync(MediaType.Illustration).WaitAsync(TimeSpan.FromSeconds(5))).JobFound);
            Assert.Equal(2, provider.Calls);
            var progress = await third.GetProgressAsync(1, 1);
            Assert.Equal(2, progress.Images.RunningCount);
            Assert.Equal(1, progress.Images.QueuedCount);
            Assert.Equal(StoryStatus.MediaProcessing, store.Items<Story>().Single().Status);
            provider.Release(0);
            Assert.True((await task1).Success);
            var task3 = third.ProcessBranchNextAsync(MediaType.Illustration);
            await provider.Started(2);
            Assert.Equal(2, store.Items<MediaAssetGenerationTask>().Count(x => x.Status == GenerationJobStatus.Processing));
            provider.Release(2);
            Assert.True((await task3).Success);
            provider.Release(1);
            Assert.True((await task2).Success);
            Assert.True((await first.RunPackageAsync()).Success);
            Assert.Equal(3, provider.Calls);
            Assert.All(store.Items<MediaAssetGenerationTask>(), x => Assert.Equal(GenerationJobStatus.Completed, x.Status));
            Assert.Equal(3, store.Items<MediaAssetGenerationTask>().Select(x => x.MediaAssetId).Distinct().Count());
        }
        finally { provider.ReleaseAll(); await Task.WhenAll(task1, task2); }
    }

    private sealed class BlockingEvaluators(bool safe) : IMediaAlignmentEvaluator, IMediaSafetyEvaluator
    {
        public TaskCompletionSource AlignmentStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SafetyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<MediaEvaluationResult> IMediaAlignmentEvaluator.EvaluateAsync(SceneSpecification spec, GeneratedMedia media, CancellationToken ct)
        {
            AlignmentStarted.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return new(MediaEvaluationDecision.Pass);
        }
        async Task<MediaEvaluationResult> IMediaSafetyEvaluator.EvaluateAsync(SceneSpecification spec, GeneratedMedia media, CancellationToken ct)
        {
            SafetyStarted.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return new(safe ? MediaEvaluationDecision.Pass : MediaEvaluationDecision.Fail, safe ? null : "unsafe");
        }
    }

    private sealed class MutationImage(Action mutate) : IImageGenerationProvider
    {
        public Task<GeneratedMedia> GenerateAsync(SceneSpecification spec, CancellationToken ct = default)
        {
            mutate();
            return Task.FromResult(new GeneratedMedia([1, 2, 3], "image/png"));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ImageTaskCannotPublishAResultAfterItsVersionOrPackageRevisionChanges(bool changeRevision)
    {
        var store = Seed();
        var version = store.Items<StoryVersion>().Single();
        var storage = new RecordingMediaStorage();
        var service = Create(store, image: new MutationImage(() =>
        {
            if (changeRevision) version.MediaState.MediaPackageRevision++;
            else version.IsCurrent = false;
        }), storage: storage);
        await service.ProcessNextAsync();
        Assert.False((await service.ProcessBranchNextAsync(MediaType.Illustration)).Success);
        Assert.Empty(storage.UploadedPaths);
        Assert.DoesNotContain(store.Items<MediaAsset>(), x => x.Status == MediaStatus.Ready || x.Url != null);
        Assert.Equal(StoryStatus.MediaProcessing, store.Items<Story>().Single().Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ParallelEvaluationWaitsForBothAndNeverUploadsAnUnsafeImage(bool safe)
    {
        var store = Seed();
        var evaluators = new BlockingEvaluators(safe);
        var storage = new RecordingMediaStorage();
        var service = Create(store, evaluator: evaluators, storage: storage,
            options: new MediaGenerationOptions { ParallelImageEvaluation = true });
        await service.ProcessNextAsync();
        var work = service.ProcessBranchNextAsync(MediaType.Illustration);
        try
        {
            await Task.WhenAll(evaluators.AlignmentStarted.Task, evaluators.SafetyStarted.Task).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(storage.UploadedPaths);
            Assert.DoesNotContain(store.Items<MediaAsset>(), x => x.Status == MediaStatus.Ready);
        }
        finally { evaluators.Release.TrySetResult(); }
        Assert.Equal(safe, (await work).Success);
        Assert.Equal(safe ? 1 : 0, storage.UploadedPaths.Count);
        Assert.Equal(safe ? 1 : 0, store.Items<MediaAsset>().Count(x => x.Status == MediaStatus.Ready));
    }

    [Fact]
    public async Task ReconciliationRecoversOldPreparedImageBranchWithoutDuplicatingReadyAssets()
    {
        var store = Seed();
        var image = new SwitchableImage();
        var service = Create(store, image: image);
        await service.ProcessNextAsync();
        await service.ProcessBranchNextAsync(MediaType.Illustration);
        var ready = store.Items<MediaAsset>().Single(x => x.Status == MediaStatus.Ready);
        foreach (var task in store.Items<MediaAssetGenerationTask>().ToArray())
            store.Repository<MediaAssetGenerationTask>().Delete(task);
        var branch = store.Items<StoryGenerationJob>().Single(x => x.Operation == GenerationJobOperation.GenerateIllustration);
        branch.Status = GenerationJobStatus.Pending;
        Assert.True((await service.RunPackageAsync()).Success);
        Assert.Equal(2, image.Calls);
        Assert.Equal(ready.Url, store.Items<MediaAsset>().Single(x => x.Id == ready.Id).Url);
        Assert.Single(store.Items<MediaAssetGenerationTask>());
    }

    [Fact]
    public async Task ReconciliationFailsAnInvalidLegacyHandoffInsteadOfRetryingPreparationForever()
    {
        var store = Seed();
        var image = new SwitchableImage();
        var service = Create(store, image: image);
        await service.ProcessNextAsync();
        foreach (var task in store.Items<MediaAssetGenerationTask>().ToArray())
            store.Repository<MediaAssetGenerationTask>().Delete(task);
        store.Items<StoryVersion>().Single().IsCurrent = false;
        await service.ProcessNextAsync();
        Assert.Equal(GenerationJobStatus.Failed, store.Items<StoryGenerationJob>()
            .Single(x => x.Operation == GenerationJobOperation.GenerateMediaPackage).Status);
        Assert.Equal(0, image.Calls);
    }
}
