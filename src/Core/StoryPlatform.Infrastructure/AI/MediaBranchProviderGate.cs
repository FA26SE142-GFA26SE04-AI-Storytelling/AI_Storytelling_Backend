using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaStorage.Models;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>One singleton per modality. Also covers SDK providers outside the HTTP pipeline.</summary>
internal sealed class MediaBranchProviderGate(MediaBranchOptions options)
{
    private readonly MediaRequestRateLimiter _limiter = new(new MediaGenerationOptions { RateLimit = options.RateLimit });
    private readonly object _sync = new();
    private int _failures;
    private DateTimeOffset _openUntil;

    public async Task<GeneratedMedia> ExecuteAsync(Func<CancellationToken, Task<GeneratedMedia>> call, CancellationToken ct)
    {
        lock (_sync)
            if (_openUntil > DateTimeOffset.UtcNow)
                throw new TransientMediaGenerationException("MEDIA_CIRCUIT_OPEN", retryAfter: _openUntil - DateTimeOffset.UtcNow);
        await _limiter.WaitAsync(ct);
        try
        {
            var result = await call(ct);
            lock (_sync) _failures = 0;
            return result;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is not PermanentMediaGenerationException)
        {
            lock (_sync)
            {
                if (++_failures >= Math.Max(2, options.CircuitBreaker.FailureThreshold))
                    _openUntil = DateTimeOffset.UtcNow.AddSeconds(Math.Max(10, options.CircuitBreaker.DurationOfBreakSeconds));
            }
            throw;
        }
    }
}

internal sealed class GatedImageProvider(IImageGenerationProvider inner, MediaBranchProviderGate gate) : IImageGenerationProvider
{
    public Task<GeneratedMedia> GenerateAsync(SceneSpecification specification, CancellationToken cancellationToken = default) =>
        gate.ExecuteAsync(ct => inner.GenerateAsync(specification, ct), cancellationToken);
}

internal sealed class GatedTtsProvider(ITtsProvider inner, MediaBranchProviderGate gate) : ITtsProvider
{
    public Task<GeneratedMedia> GenerateAsync(string text, CancellationToken cancellationToken = default) =>
        gate.ExecuteAsync(ct => inner.GenerateAsync(text, ct), cancellationToken);
}
