using System.Net;
using StoryPlatform.AI.Application.OutlineGeneration;

namespace StoryPlatform.AI.Api.Middleware;

public sealed class AIExceptionHandlingMiddleware
{
    /// <summary>Exception.Data key carrying the provider-requested backoff in seconds.</summary>
    private const string RetryAfterSecondsDataKey = "RetryAfterSeconds";

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
        catch (StoryPlatform.AI.Application.Moderation.GrayZoneAiException exception)
        {
            _logger.LogWarning("Gray zone evaluation failed: {ErrorCode}", exception.Code);
            await WriteErrorAsync(context, (HttpStatusCode)exception.StatusCode, "Gray zone evaluation is unavailable.", exception.Code);
        }
        catch (StoryPlatform.AI.Application.WishCards.WishCardOutputException)
        {
            _logger.LogWarning("Wish card matching output rejected.");
            await WriteErrorAsync(context, HttpStatusCode.BadGateway, "Wish card matching output failed validation.", "WISH_CARD_OUTPUT_INVALID");
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
        catch (StoryPlatform.AI.Domain.Generation.LlmOutputTruncatedException exception)
        {
            // Not a retryable provider fault: the same request under the same ceiling truncates again.
            _logger.LogError(exception, "AI provider stopped at the configured output token limit. Path={Path} TraceId={TraceId}",
                context.Request.Path.Value, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier);
            await WriteErrorAsync(context, HttpStatusCode.BadGateway, "AI output exceeded the configured token budget.", "AI_OUTPUT_TRUNCATED");
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
        catch (System.Text.Json.JsonException exception)
        {
            var validationCode = StoryPlatform.Contracts.AI.Models.LearningValidationFeedback.Normalize(exception.Message)
                ?? "LEARNING_JSON_INVALID";
            int? itemIndex = exception.Data["LearningItemIndex"] is int index && index is >= 0 and < 40 ? index : null;
            _logger.LogWarning("AI output rejected. ValidationCode={ValidationCode} ItemIndex={ItemIndex} Path={Path} TraceId={TraceId}",
                validationCode, itemIndex, context.Request.Path.Value, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier);
            await WriteErrorAsync(context, HttpStatusCode.BadGateway, "Generated learning output failed validation.", "AI_OUTPUT_INVALID", validationCode, itemIndex);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(exception, "AI generation service is not configured or returned invalid output.");
            await WriteErrorAsync(context, HttpStatusCode.ServiceUnavailable, "AI generation is currently unavailable.");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "AI provider request failed.");
            var permanent = exception.StatusCode is not null && (int)exception.StatusCode is >= 400 and < 500
                && exception.StatusCode is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests);
            // Forward the provider's requested backoff so Core can wait instead of retrying into the same window.
            if (exception.Data[RetryAfterSecondsDataKey] is double retryAfterSeconds && retryAfterSeconds > 0)
                context.Response.Headers.RetryAfter = Math.Ceiling(retryAfterSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await WriteErrorAsync(context, permanent ? HttpStatusCode.BadRequest : exception.StatusCode == HttpStatusCode.TooManyRequests ? HttpStatusCode.TooManyRequests : HttpStatusCode.BadGateway,
                "The AI provider request failed.", permanent ? "AI_PROVIDER_CONFIGURATION_REJECTED" : exception.StatusCode == HttpStatusCode.TooManyRequests ? "AI_PROVIDER_429" : "AI_PROVIDER_FAILED");
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
        string? errorCode = null,
        string? validationCode = null,
        int? itemIndex = null)
    {
        if (context.RequestAborted.IsCancellationRequested || context.Response.HasStarted) return;
        context.Response.StatusCode = (int)statusCode;
        try
        {
            await context.Response.WriteAsJsonAsync(new { errorCode, error = message, validationCode, itemIndex,
                traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier }, cancellationToken: context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    }
}
