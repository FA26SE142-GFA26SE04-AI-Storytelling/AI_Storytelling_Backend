using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Shared transport retry helper for outbound Gemini REST calls.
/// Honors 429 / 5xx / HttpRequestException / TaskCanceledException.
/// TransportRetryCount counts only transient failures; permanent failures (4xx other than 429)
/// throw immediately so the caller can classify them as PermanentMediaGenerationException.
/// </summary>
internal static class GeminiHttpRetry
{
    public static async Task<HttpResponseMessage> SendWithTransportRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        HttpClient httpClient,
        int retryCount,
        TimeSpan baseDelay,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (retryCount < 0) retryCount = 0;

        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt <= retryCount; attempt++)
        {
            using var request = requestFactory();
            try
            {
                response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (IsTransient(response.StatusCode) && attempt < retryCount)
                {
                    logger.LogWarning(
                        "Gemini transient status {Status}; retry {Next}/{Max} after {Delay}ms",
                        (int)response.StatusCode, attempt + 1, retryCount, baseDelay.TotalMilliseconds);
                    response.Dispose();
                    await DelayBackoffAsync(attempt, baseDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                return response;
            }
            catch (HttpRequestException) when (attempt < retryCount && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Gemini HTTP request exception; retry {Next}/{Max}", attempt + 1, retryCount);
                response?.Dispose();
                await DelayBackoffAsync(attempt, baseDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < retryCount)
            {
                // Client-side timeout, not user cancellation.
                logger.LogWarning("Gemini request timeout; retry {Next}/{Max}", attempt + 1, retryCount);
                response?.Dispose();
                await DelayBackoffAsync(attempt, baseDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("GEMINI_TRANSPORT_RETRIES_EXHAUSTED");
    }

    private static bool IsTransient(System.Net.HttpStatusCode code) =>
        code == System.Net.HttpStatusCode.TooManyRequests ||
        code == System.Net.HttpStatusCode.RequestTimeout ||
        (int)code >= 500;

    private static async Task DelayBackoffAsync(int attempt, TimeSpan baseDelay, CancellationToken cancellationToken)
    {
        // Exponential with full jitter: delay = random(0..base * 2^attempt).
        var multiplier = Math.Pow(2, attempt);
        var max = baseDelay.TotalMilliseconds * multiplier;
        var jitter = Random.Shared.NextDouble() * max;
        await Task.Delay(TimeSpan.FromMilliseconds(jitter), cancellationToken).ConfigureAwait(false);
    }
}
