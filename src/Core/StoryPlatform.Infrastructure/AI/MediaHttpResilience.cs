using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using StoryPlatform.Application.Features.MediaGeneration;

namespace StoryPlatform.Infrastructure.AI;

public static class MediaHttpResilience
{
    public static IHttpClientBuilder AddMediaResiliencePipeline(
        this IHttpClientBuilder client,
        MediaGenerationOptions options,
        int maxRetryAttempts = 3,
        int retryBaseDelayMilliseconds = 500)
    {
        var retryable = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(response =>
                response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                response.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
                (int)response.StatusCode >= 500);
        var circuitFailures = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(response =>
                response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or
                    System.Net.HttpStatusCode.ServiceUnavailable);

        client.AddResilienceHandler("media", pipeline =>
        {
            pipeline.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = circuitFailures,
                FailureRatio = Math.Clamp(options.CircuitBreaker.FailureRatio, 0.01d, 1d),
                MinimumThroughput = Math.Max(2, options.CircuitBreaker.FailureThreshold),
                SamplingDuration = TimeSpan.FromSeconds(Math.Max(10, options.CircuitBreaker.SamplingDurationSeconds)),
                BreakDuration = TimeSpan.FromSeconds(Math.Max(10, options.CircuitBreaker.DurationOfBreakSeconds))
            });
            if (maxRetryAttempts > 0)
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    ShouldHandle = retryable,
                    MaxRetryAttempts = Math.Clamp(maxRetryAttempts, 1, 10),
                    Delay = TimeSpan.FromMilliseconds(Math.Max(1, retryBaseDelayMilliseconds)),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldRetryAfterHeader = true
                });
            }
        });
        return client;
    }
}
