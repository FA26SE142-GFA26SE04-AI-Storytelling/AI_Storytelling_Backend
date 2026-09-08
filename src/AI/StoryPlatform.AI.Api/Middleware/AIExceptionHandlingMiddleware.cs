using System.Net;

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

    private static async Task WriteErrorAsync(HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsJsonAsync(new { error = message });
    }
}
