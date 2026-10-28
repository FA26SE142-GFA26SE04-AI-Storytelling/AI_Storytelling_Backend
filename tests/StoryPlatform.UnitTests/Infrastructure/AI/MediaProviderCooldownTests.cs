using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.AI;

public sealed class MediaProviderCooldownTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static MediaBranchProviderGate Gate(Clock clock) => new(new MediaBranchOptions
    {
        RateLimit = new() { BurstSize = 100 },
        QuotaRetryBaseDelaySeconds = 10,
        QuotaRetryMaxDelaySeconds = 40,
    }, modality: "Image", clock: clock);

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    public async Task Quota_failure_defers_all_workers_and_resumes_after_shared_deadline(int retryAfterSeconds)
    {
        var clock = new Clock();
        var gate = Gate(clock);
        var calls = 0;
        Task<GeneratedMedia> Fail(CancellationToken _) { calls++; throw new TransientMediaGenerationException("GEMINI_IMAGE_HTTP_429",
            retryAfter: retryAfterSeconds == 0 ? null : TimeSpan.FromSeconds(retryAfterSeconds)); }
        var first = await Assert.ThrowsAsync<TransientMediaGenerationException>(() => gate.ExecuteAsync(Fail, default));
        Assert.True(first.RetryAfter >= TimeSpan.FromSeconds(Math.Max(10, retryAfterSeconds)));
        var blocked = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            await Assert.ThrowsAsync<TransientMediaGenerationException>(() => gate.ExecuteAsync(Fail, default))));
        Assert.All(blocked, ex => Assert.Equal("MEDIA_PROVIDER_COOLDOWN", ex.ErrorCode));
        Assert.Equal(1, calls);
        clock.Now += first.RetryAfter!.Value + TimeSpan.FromMilliseconds(1);
        var second = await Assert.ThrowsAsync<TransientMediaGenerationException>(() => gate.ExecuteAsync(Fail, default));
        Assert.True(second.RetryAfter >= TimeSpan.FromSeconds(20));
        clock.Now += second.RetryAfter!.Value + TimeSpan.FromMilliseconds(1);
        await gate.ExecuteAsync(_ => Task.FromResult(new GeneratedMedia([1], "image/png")), default);
        var reset = await Assert.ThrowsAsync<TransientMediaGenerationException>(() => gate.ExecuteAsync(Fail, default));
        if (retryAfterSeconds == 0) Assert.InRange(reset.RetryAfter!.Value.TotalSeconds, 10, 12.5);
    }

    [Fact]
    public async Task Worker_already_waiting_for_a_rate_token_rechecks_shared_cooldown()
    {
        var gate = new MediaBranchProviderGate(new() { RateLimit = new() { BurstSize = 1, RequestsPerMinute = 60 } }, modality: "Image");
        var firstResponse = new TaskCompletionSource<GeneratedMedia>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var first = gate.ExecuteAsync(_ => { Interlocked.Increment(ref calls); return firstResponse.Task; }, default);
        var second = gate.ExecuteAsync(_ => { Interlocked.Increment(ref calls); return Task.FromResult(new GeneratedMedia([1], "image/png")); }, default);
        firstResponse.SetException(new TransientMediaGenerationException("GEMINI_IMAGE_HTTP_429"));
        await Assert.ThrowsAsync<TransientMediaGenerationException>(() => first);
        var blocked = await Assert.ThrowsAsync<TransientMediaGenerationException>(() => second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("MEDIA_PROVIDER_COOLDOWN", blocked.ErrorCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_rate_token_never_calls_provider()
    {
        var gate = new MediaBranchProviderGate(new() { RateLimit = new() { BurstSize = 1, RequestsPerMinute = 1 } }, modality: "Image");
        await gate.ExecuteAsync(_ => Task.FromResult(new GeneratedMedia([1], "image/png")), default);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.ExecuteAsync(_ =>
            throw new InvalidOperationException("Provider must not be called"), cts.Token));
    }

    [Fact]
    public async Task Older_inflight_success_does_not_clear_a_new_cooldown()
    {
        var clock = new Clock();
        var gate = Gate(clock);
        var pending = new TaskCompletionSource<GeneratedMedia>(TaskCreationOptions.RunContinuationsAsynchronously);
        var older = gate.ExecuteAsync(_ => pending.Task, default);
        await Assert.ThrowsAsync<TransientMediaGenerationException>(() => gate.ExecuteAsync(_ =>
            throw new TransientMediaGenerationException("GEMINI_IMAGE_HTTP_429"), default));
        pending.SetResult(new GeneratedMedia([1], "image/png"));
        await older;
        var blocked = await Assert.ThrowsAsync<TransientMediaGenerationException>(() => gate.ExecuteAsync(_ =>
            throw new InvalidOperationException("Provider must not be called"), default));
        Assert.Equal("MEDIA_PROVIDER_COOLDOWN", blocked.ErrorCode);
    }

    [Fact]
    public async Task Circuit_open_response_blocks_other_workers_without_calling_provider()
    {
        var clock = new Clock();
        var gate = Gate(clock);
        await Assert.ThrowsAsync<TransientMediaGenerationException>(() => gate.ExecuteAsync(_ =>
            throw new TransientMediaGenerationException("MEDIA_CIRCUIT_OPEN", retryAfter: TimeSpan.FromSeconds(25)), default));
        var blocked = await Assert.ThrowsAsync<TransientMediaGenerationException>(() => gate.ExecuteAsync(_ =>
            throw new InvalidOperationException("Provider must not be called"), default));
        Assert.Equal("MEDIA_CIRCUIT_OPEN", blocked.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(25), blocked.RetryAfter);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.ExecuteAsync(_ =>
            throw new InvalidOperationException("Provider must not be called"), cts.Token));
    }
}
