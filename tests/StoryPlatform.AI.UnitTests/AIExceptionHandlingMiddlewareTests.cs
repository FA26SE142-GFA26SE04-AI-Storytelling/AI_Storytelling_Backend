using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StoryPlatform.AI.Api.Middleware;
using Xunit;

namespace StoryPlatform.AI.UnitTests;

public sealed class AIExceptionHandlingMiddlewareTests
{
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
