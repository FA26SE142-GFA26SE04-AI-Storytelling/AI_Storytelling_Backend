using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaStorage.Models;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>One singleton per modality. Also covers SDK providers outside the HTTP pipeline.</summary>
internal sealed class MediaBranchProviderGate(MediaBranchOptions options, ILogger? logger = null, string modality = "Media", TimeProvider? clock = null)
{
    private readonly MediaRequestRateLimiter _limiter = new(new MediaGenerationOptions { RateLimit = options.RateLimit });
    private readonly object _sync = new();
    private int _failures;
    private DateTimeOffset _openUntil;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private DateTimeOffset _cooldownUntil;
    private int _quotaFailures;

    private void CheckAvailability()
    {
        lock (_sync)
        {
            var now = _clock.GetUtcNow();
            if (_openUntil > now)
                throw new TransientMediaGenerationException("MEDIA_CIRCUIT_OPEN", retryAfter: _openUntil - now);
            if (_cooldownUntil > now)
                throw new TransientMediaGenerationException("MEDIA_PROVIDER_COOLDOWN", retryAfter: _cooldownUntil - now);
        }
    }

    public async Task<GeneratedMedia> ExecuteAsync(Func<CancellationToken, Task<GeneratedMedia>> call, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        CheckAvailability();
        var wait = Stopwatch.StartNew();
        await _limiter.WaitAsync(ct);
        logger?.LogInformation("Media provider wait Modality={Modality} DurationMs={DurationMs}", modality, wait.Elapsed.TotalMilliseconds);
        CheckAvailability();
        try
        {
            var result = await call(ct);
            lock (_sync)
            {
                _failures = 0;
                // An older in-flight success must not cancel a newer quota cooldown.
                if (_cooldownUntil <= _clock.GetUtcNow()) _quotaFailures = 0;
            }
            return result;
        }
        catch (TransientMediaGenerationException ex) when (!ct.IsCancellationRequested &&
            modality == "Image" && ex.ErrorCode == "GEMINI_IMAGE_HTTP_429")
        {
            TimeSpan delay;
            lock (_sync)
            {
                var baseSeconds = Math.Clamp(options.QuotaRetryBaseDelaySeconds, 1, 600);
                var maxSeconds = Math.Clamp(options.QuotaRetryMaxDelaySeconds, baseSeconds, 3600);
                var seconds = Math.Min(maxSeconds, baseSeconds * Math.Pow(2, Math.Min(_quotaFailures++, 8)));
                delay = TimeSpan.FromSeconds(seconds + Random.Shared.NextDouble() * Math.Min(5, seconds * 0.25));
                if (ex.RetryAfter is { } after && after > delay) delay = after;
                var until = _clock.GetUtcNow().Add(delay);
                if (until > _cooldownUntil) _cooldownUntil = until;
                delay = _cooldownUntil - _clock.GetUtcNow();
            }
            logger?.LogWarning("Media provider cooldown Modality={Modality} Code={Code} RetryDelayMs={RetryDelayMs}",
                modality, ex.ErrorCode, delay.TotalMilliseconds);
            throw new TransientMediaGenerationException(ex.ErrorCode, ex, delay);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is not PermanentMediaGenerationException)
        {
            lock (_sync)
            {
                if (ex is TransientMediaGenerationException { ErrorCode: "MEDIA_CIRCUIT_OPEN" } open)
                    _openUntil = _clock.GetUtcNow().Add(open.RetryAfter ?? TimeSpan.FromSeconds(Math.Max(10, options.CircuitBreaker.DurationOfBreakSeconds)));
                else if (++_failures >= Math.Max(2, options.CircuitBreaker.FailureThreshold))
                    _openUntil = _clock.GetUtcNow().AddSeconds(Math.Max(10, options.CircuitBreaker.DurationOfBreakSeconds));
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
