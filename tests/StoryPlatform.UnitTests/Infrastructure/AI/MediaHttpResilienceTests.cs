using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.AI;

public sealed class MediaHttpResilienceTests
{
    [Fact]
    public async Task Retry_honors_retry_after_before_exponential_delay()
    {
        var calls = 0;
        var handler = new MockHandler((request, cancellationToken) =>
        {
            calls++;
            if (calls == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(response);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var client = CreateClient(handler, new MediaGenerationOptions(), 1, 5_000);
        var timer = Stopwatch.StartNew();

        using var response = await client.GetAsync("https://example.test/media");

        timer.Stop();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, calls);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), $"Retry-After was ignored; elapsed {timer.Elapsed}.");
    }

    [Fact]
    public async Task Retry_handles_request_timeout_response()
    {
        var calls = 0;
        var handler = new MockHandler((request, cancellationToken) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(
                calls == 1 ? HttpStatusCode.RequestTimeout : HttpStatusCode.OK));
        });
        var client = CreateClient(handler, new MediaGenerationOptions(), 1, 1);

        using var response = await client.GetAsync("https://example.test/media");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Circuit_breaker_opens_after_configured_429_or_503_failures()
    {
        var calls = 0;
        var handler = new MockHandler((request, cancellationToken) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });
        var options = new MediaGenerationOptions
        {
            CircuitBreaker = new CircuitBreakerConfig
            {
                FailureThreshold = 2,
                SamplingDurationSeconds = 10,
                DurationOfBreakSeconds = 10
            }
        };
        var client = CreateClient(handler, options, 0, 1);

        using var first = await client.GetAsync("https://example.test/media");
        using var second = await client.GetAsync("https://example.test/media");
        await Assert.ThrowsAsync<BrokenCircuitException>(() =>
            client.GetAsync("https://example.test/media"));

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Vertex_auth_handler_skips_adc_when_public_gemini_mode_is_selected()
    {
        var inner = new MockHandler((request, cancellationToken) =>
        {
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var handler = new VertexAuthDelegatingHandler(
            null!, Options.Create(new VertexOptions { UseVertex = false }))
        {
            InnerHandler = inner
        };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://example.test/media");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpClient CreateClient(
        HttpMessageHandler handler,
        MediaGenerationOptions options,
        int retryCount,
        int delayMilliseconds)
    {
        var services = new ServiceCollection();
        var builder = services.AddHttpClient("resilience-test")
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        builder.AddMediaResiliencePipeline(options, retryCount, delayMilliseconds);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>()
            .CreateClient("resilience-test");
    }

    private sealed class MockHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => callback(request, cancellationToken);
    }
}
