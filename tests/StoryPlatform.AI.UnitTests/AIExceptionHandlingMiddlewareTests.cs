using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StoryPlatform.AI.Api.Middleware;
using Xunit;

namespace StoryPlatform.AI.UnitTests;

public sealed class AIExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task Quiz_validation_includes_item_index_without_exposing_the_question()
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        var error = new JsonException("QUIZ_HINT_REQUIRED"); error.Data["LearningItemIndex"] = 3;
        var logger = new RecordingLogger<AIExceptionHandlingMiddleware>();
        await new AIExceptionHandlingMiddleware(_ => throw error, logger).InvokeAsync(context);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(502, context.Response.StatusCode);
        Assert.Equal("QUIZ_HINT_REQUIRED", body.RootElement.GetProperty("validationCode").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("itemIndex").GetInt32());
        Assert.Contains(logger.Entries, x => x.Message.Contains("ItemIndex=3"));
    }

    [Theory]
    [InlineData("QUIZ_OPTIONS_INVALID", "QUIZ_OPTIONS_INVALID")]
    [InlineData("LEARNING_EVIDENCE_NOT_IN_STORY", "LEARNING_EVIDENCE_NOT_IN_STORY")]
    [InlineData("private story text and token", "LEARNING_JSON_INVALID")]
    public async Task Output_validation_reports_safe_code_and_trace_without_logging_payload(string message, string expectedCode)
    {
        var context = new DefaultHttpContext { TraceIdentifier = "validation-trace" };
        context.Response.Body = new MemoryStream();
        var logger = new RecordingLogger<AIExceptionHandlingMiddleware>();
        await new AIExceptionHandlingMiddleware(_ => throw new JsonException(message), logger).InvokeAsync(context);
        Assert.Equal(502, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("AI_OUTPUT_INVALID", body.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(expectedCode, body.RootElement.GetProperty("validationCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
        Assert.Contains(logger.Entries, x => x.Level == LogLevel.Warning && x.Message.Contains(expectedCode));
        Assert.DoesNotContain(logger.Entries, x => x.Message.Contains("private story"));
        Assert.DoesNotContain("private story", body.RootElement.GetRawText());
    }

    [Theory]
    [InlineData(400, 400, "AI_PROVIDER_CONFIGURATION_REJECTED")]
    [InlineData(403, 400, "AI_PROVIDER_CONFIGURATION_REJECTED")]
    [InlineData(429, 429, "AI_PROVIDER_429")]
    [InlineData(500, 502, "AI_PROVIDER_FAILED")]
    public async Task Provider_errors_keep_retryable_and_permanent_failures_distinct(int upstream, int expected, string code)
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        await new AIExceptionHandlingMiddleware(_ => throw new HttpRequestException("private provider body", null, (System.Net.HttpStatusCode)upstream),
            new RecordingLogger<AIExceptionHandlingMiddleware>()).InvokeAsync(context);
        Assert.Equal(expected, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(code, body.RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("private provider body", body.RootElement.GetRawText());
    }

    [Fact]
    public async Task Provider_backoff_is_forwarded_as_retry_after_header()
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        var error = new HttpRequestException("Sensitive AI provider failed.", null, System.Net.HttpStatusCode.TooManyRequests);
        error.Data["RetryAfterSeconds"] = 21.4;
        await new AIExceptionHandlingMiddleware(_ => throw error, new RecordingLogger<AIExceptionHandlingMiddleware>()).InvokeAsync(context);
        Assert.Equal(429, context.Response.StatusCode);
        Assert.Equal("22", context.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task Missing_provider_backoff_leaves_retry_after_unset()
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        await new AIExceptionHandlingMiddleware(_ => throw new HttpRequestException("Sensitive AI provider failed.", null, System.Net.HttpStatusCode.TooManyRequests),
            new RecordingLogger<AIExceptionHandlingMiddleware>()).InvokeAsync(context);
        Assert.Equal(429, context.Response.StatusCode);
        Assert.True(string.IsNullOrEmpty(context.Response.Headers.RetryAfter.ToString()));
    }

    [Fact]
    public async Task DisconnectedCallerDoesNotReceiveResponseOrUnhandledError()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cancelled.Token };
        context.Response.Body = new MemoryStream();
        var logger = new RecordingLogger<AIExceptionHandlingMiddleware>();
        await new AIExceptionHandlingMiddleware(_ => throw new TaskCanceledException(), logger).InvokeAsync(context);
        Assert.Equal(0, context.Response.Body.Length);
        Assert.DoesNotContain(logger.Entries, x => x.Level == LogLevel.Error);
        Assert.Contains(logger.Entries, x => x.Message.Contains("aborted by caller"));
    }

    [Theory]
    [InlineData("provider-cancel", 503, "AI_SERVICE_UNAVAILABLE")]
    [InlineData("timeout", 504, "AI_TIMEOUT")]
    [InlineData("unexpected", 500, null)]
    public async Task ConnectedCallerReceivesClassifiedError(string kind, int status, string? code)
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        var logger = new RecordingLogger<AIExceptionHandlingMiddleware>();
        Exception exception = kind switch { "provider-cancel" => new TaskCanceledException("private data"), "timeout" => new TimeoutException("private data"), _ => new Exception("unexpected") };
        await new AIExceptionHandlingMiddleware(_ => throw exception, logger).InvokeAsync(context);
        Assert.Equal(status, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(code, body.RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("private data", body.RootElement.GetRawText());
        if (kind != "unexpected") Assert.DoesNotContain(logger.Entries, x => x.Level == LogLevel.Error);
        else Assert.Contains(logger.Entries, x => x.Level == LogLevel.Error);
    }

    [Fact]
    public async Task TruncatedOutputIsReportedAsItsOwnCodeRatherThanAJsonFailure()
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        var logger = new RecordingLogger<AIExceptionHandlingMiddleware>();
        await new AIExceptionHandlingMiddleware(_ => throw new StoryPlatform.AI.Domain.Generation.LlmOutputTruncatedException(
            "secret prompt"), logger).InvokeAsync(context);
        Assert.Equal(502, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        // The operator needs to see capacity, not a JSON bug: retrying truncation just re-burns quota.
        Assert.Equal("AI_OUTPUT_TRUNCATED", body.RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("secret prompt", body.RootElement.GetRawText());
        Assert.Contains(logger.Entries, x => x.Level == LogLevel.Error && x.Message.Contains("output token limit"));
    }

    [Fact]
    public async Task DisconnectDuringProviderFailureDoesNotWriteResponse()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cancelled.Token }; context.Response.Body = new MemoryStream();
        await new AIExceptionHandlingMiddleware(_ => throw new HttpRequestException(), new RecordingLogger<AIExceptionHandlingMiddleware>()).InvokeAsync(context);
        Assert.Equal(0, context.Response.Body.Length);
    }
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter) => Entries.Add((level, formatter(state, error)));
}
