using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Features.ContentGeneration;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration;

namespace StoryPlatform.Infrastructure.AI;

public sealed class LearningBatchRunner(IServiceScopeFactory scopes, LearningBatchOptions options) : ILearningBatchRunner
{
    public async Task RunAsync(IReadOnlyList<int> batchIds, string rootClaimToken, CancellationToken ct)
    {
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        // Every batch owns a separate application service + DbContext, even while another batch is persisting.
        await Parallel.ForEachAsync(batchIds, new ParallelOptions
        { MaxDegreeOfParallelism = Math.Clamp(options.MaxConcurrentPerStory, 1, 2), CancellationToken = ct }, async (id, token) =>
        {
            await using var scope = scopes.CreateAsyncScope();
            try { await scope.ServiceProvider.GetRequiredService<ILearningBatchProcessor>().ProcessAsync(id, rootClaimToken, token); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { errors.Enqueue(ex); }
        });
        if (!errors.IsEmpty) throw new AggregateException("LEARNING_BATCHES_FAILED", errors);
    }
}

public sealed class LearningRequestGate(LearningBatchOptions options) : ILearningRequestGate, IDisposable
{
    private readonly SemaphoreSlim _slots = new(Math.Clamp(options.MaxConcurrentRequests, 1, 8));
    private readonly MediaRequestRateLimiter _rate = new(new MediaGenerationOptions { RateLimit = new() { RequestsPerMinute = options.RequestsPerMinute, BurstSize = options.BurstSize } });
    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        await _slots.WaitAsync(ct);
        try { await _rate.WaitAsync(ct); return new Permit(_slots); }
        catch { _slots.Release(); throw; }
    }
    private sealed class Permit(SemaphoreSlim slots) : IDisposable
    {
        private int _released;
        public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) slots.Release(); }
    }
    public void Dispose() => _slots.Dispose();
}
