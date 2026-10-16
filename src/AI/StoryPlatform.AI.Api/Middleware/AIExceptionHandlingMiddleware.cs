using System.Net;
using StoryPlatform.AI.Application.OutlineGeneration;

namespace StoryPlatform.AI.Api.Middleware;

public sealed class AIExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AIExceptionHandlingMiddleware> _logger;

    public AIExceptionHandlingMiddleware(RequestDelegate next, ILogger<AIExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation("AI request aborted by caller. TraceId={TraceId}", System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier);
            // The caller is gone. Do not write an error response to the aborted connection.
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("AI upstream timed out. TraceId={TraceId}", System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier);
            await WriteErrorAsync(context, HttpStatusCode.GatewayTimeout, "AI generation timed out.", "AI_TIMEOUT");
        }
        catch (OperationCanceledException)
        {
            // An SDK may cancel independently of RequestAborted; that alone does not prove a timeout.
            _logger.LogWarning("AI provider cancelled with caller still connected. TraceId={TraceId}", System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier);
            await WriteErrorAsync(context, HttpStatusCode.ServiceUnavailable, "The AI provider request was cancelled.", "AI_SERVICE_UNAVAILABLE");
        }
        catch (StoryPlatform.AI.Application.CreatorAssistance.AssistanceOutputException exception)
        {
            _logger.LogWarning("Creator assistance output rejected: {ErrorCode}", exception.Code);
            await WriteErrorAsync(context, HttpStatusCode.BadGateway, "Creator assistance output could not be accepted.", exception.Code);
        }
        catch (OutlineRejectedException exception)
        {
            _logger.LogWarning(exception, "Generated outline failed output safety validation: {ReasonCode}.", exception.ReasonCode);
            await WriteErrorAsync(context, HttpStatusCode.UnprocessableEntity, exception.Message, exception.ReasonCode);
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(exception, "Invalid AI generation request.");
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(exception, "AI generation service is not configured or returned invalid output.");
            await WriteErrorAsync(context, HttpStatusCode.ServiceUnavailable, "AI generation is currently unavailable.");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "AI provider request failed.");
            await WriteErrorAsync(context, HttpStatusCode.BadGateway, "The AI provider request failed.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unhandled AI service error.");
            await WriteErrorAsync(context, HttpStatusCode.InternalServerError, "An internal AI service error occurred.");
        }
    }

    private static async Task WriteErrorAsync(
        HttpContext context,
        HttpStatusCode statusCode,
        string message,
        string? errorCode = null)
    {
        if (context.RequestAborted.IsCancellationRequested || context.Response.HasStarted) return;
        context.Response.StatusCode = (int)statusCode;
        try
        {
            await context.Response.WriteAsJsonAsync(new { errorCode, error = message }, cancellationToken: context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    }
}
