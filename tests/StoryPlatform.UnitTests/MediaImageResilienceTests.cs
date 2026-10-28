using System.Diagnostics;
using System.Text.Json;
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
    private sealed class DeferredImage(string code) : IImageGenerationProvider
    {
        public bool Block { get; set; } = true;
        public int Calls { get; private set; }
        public Task<GeneratedMedia> GenerateAsync(SceneSpecification specification, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Block) throw new TransientMediaGenerationException(code, retryAfter: TimeSpan.FromSeconds(45));
            return Task.FromResult(new GeneratedMedia([1, 2, 3], "image/png"));
        }
    }

    [Theory]
    [InlineData("MEDIA_CIRCUIT_OPEN")]
    [InlineData("MEDIA_PROVIDER_COOLDOWN")]
    public async Task Deferred_tasks_keep_attempt_budget_and_existing_ready_images(string code)
    {
        var store = Seed();
        store.Items<StoryVersion>().Single().Content = "A\n\nB";
        var provider = new DeferredImage(code) { Block = false };
        var service = Create(store, image: provider);
        await service.ProcessNextAsync();
        Assert.True((await service.ProcessBranchNextAsync(MediaType.Illustration)).Success);
        var ready = store.Items<MediaAsset>().Single(x => x.Status == MediaStatus.Ready);
        var readyUrl = ready.Url;
        provider.Block = true;
        for (var i = 0; i < 5; i++)
        {
            Assert.False((await service.ProcessBranchNextAsync(MediaType.Illustration)).Success);
            var task = store.Items<MediaAssetGenerationTask>().Single(x => x.Status == GenerationJobStatus.Pending);
            Assert.Equal(0, task.AttemptNo);
            Assert.Equal(0, store.Items<MediaAsset>().Single(x => x.Id == task.MediaAssetId).AttemptCount);
            Assert.True(task.NextAttemptAt >= task.QueuedAt.AddSeconds(45));
            task.NextAttemptAt = null;
        }
        provider.Block = false;
        Assert.True((await service.ProcessBranchNextAsync(MediaType.Illustration)).Success);
        Assert.Equal(readyUrl, ready.Url);
        Assert.Equal(1, ready.AttemptCount);
        Assert.All(store.Items<MediaAssetGenerationTask>(), task => Assert.Equal(GenerationJobStatus.Completed, task.Status));
    }

    private sealed class FaultingEvaluators(string failure) : IMediaAlignmentEvaluator, IMediaSafetyEvaluator
    {
        public bool SafetyStarted { get; private set; }
        public bool SafetyFinished { get; private set; }
        public Task<MediaEvaluationResult> EvaluateAsync(SceneSpecification specification, GeneratedMedia media, CancellationToken ct)
            => failure == "alignment" ? Task.FromException<MediaEvaluationResult>(new TransientMediaGenerationException("ALIGNMENT_EVALUATOR_HTTP_503"))
                : failure == "circuit" ? Task.FromException<MediaEvaluationResult>(new TransientMediaGenerationException("MEDIA_CIRCUIT_OPEN"))
                : Task.FromResult(new MediaEvaluationResult(MediaEvaluationDecision.Pass));
        async Task<MediaEvaluationResult> IMediaSafetyEvaluator.EvaluateAsync(SceneSpecification specification, GeneratedMedia media, CancellationToken ct)
        {
            SafetyStarted = true;
            try
            {
                if (failure == "timeout") await Task.Delay(Timeout.Infinite, ct);
                else await Task.Yield();
                if (failure == "safety") throw new TransientMediaGenerationException("SAFETY_EVALUATOR_HTTP_503");
                return new(MediaEvaluationDecision.Pass);
            }
            finally { SafetyFinished = true; }
        }
    }

    [Theory]
    [InlineData("alignment", "ALIGNMENT_EVALUATOR_HTTP_503")]
    [InlineData("safety", "SAFETY_EVALUATOR_HTTP_503")]
    [InlineData("timeout", "MEDIA_PROVIDER_TIMEOUT")]
    [InlineData("circuit", "MEDIA_CIRCUIT_OPEN")]
    public async Task Parallel_evaluator_failures_are_observed_and_never_uploaded(string failure, string code)
    {
        var store = Seed();
        var evaluator = new FaultingEvaluators(failure);
        store.Items<StoryVersion>().Single().Content = "A";
        var storage = new RecordingMediaStorage();
        var service = Create(store, evaluator: evaluator, storage: storage, options: new()
        {
            ParallelImageEvaluation = true,
            Images = new() { ProviderTimeoutSeconds = 1 }
        });
        await service.ProcessNextAsync();
        var result = await service.ProcessBranchNextAsync(MediaType.Illustration);
        Assert.False(result.Success);
        Assert.Equal(code, store.Items<MediaAssetGenerationTask>().Single().ErrorCode);
        Assert.Equal(1, store.Items<MediaAsset>().Single().AttemptCount);
        Assert.True(evaluator.SafetyStarted);
        Assert.True(evaluator.SafetyFinished);
        Assert.Empty(storage.UploadedPaths);
        Assert.DoesNotContain(store.Items<MediaAsset>(), x => x.Status == MediaStatus.Ready);
    }

    private sealed class TimedEvaluators : IMediaAlignmentEvaluator, IMediaSafetyEvaluator
    {
        public async Task<MediaEvaluationResult> EvaluateAsync(SceneSpecification spec, GeneratedMedia media, CancellationToken ct)
        { await Task.Delay(200, ct); return new(MediaEvaluationDecision.Pass); }
        async Task<MediaEvaluationResult> IMediaSafetyEvaluator.EvaluateAsync(SceneSpecification spec, GeneratedMedia media, CancellationToken ct)
        { await Task.Delay(100, ct); return new(MediaEvaluationDecision.Pass); }
    }

    [Fact]
    [Trait("Category", "MediaBenchmark")]
    public async Task Controlled_evaluation_benchmark_records_both_modes_with_identical_inputs()
    {
        var measurements = new List<object>();
        foreach (var parallel in new[] { false, true })
        {
            var durations = new List<double>();
            for (var i = 0; i < 5; i++)
            {
                var store = Seed();
                store.Items<StoryVersion>().Single().Content = "A";
                var service = Create(store, evaluator: new TimedEvaluators(), options: new() { ParallelImageEvaluation = parallel });
                await service.ProcessNextAsync();
                var timer = Stopwatch.StartNew();
                Assert.True((await service.ProcessBranchNextAsync(MediaType.Illustration)).Success);
                durations.Add(timer.Elapsed.TotalMilliseconds);
                Assert.Single(store.Items<MediaAsset>(), x => x.Status == MediaStatus.Ready);
            }
            measurements.Add(new { parallel, samples = durations.Count, averageMs = durations.Average(), p95Ms = durations.Order().Last() });
        }
        Console.WriteLine("MEDIA_CONTROLLED_BENCHMARK " + JsonSerializer.Serialize(measurements));
    }
}
