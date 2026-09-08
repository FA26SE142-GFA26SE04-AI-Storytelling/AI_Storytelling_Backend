using System.Net;

namespace StoryPlatform.AI.Api.Middleware;

public sealed class InternalApiKeyMiddleware
{
    private const string HeaderName = "X-Internal-Api-Key";
    private readonly RequestDelegate _next;
    private readonly string? _apiKey;

    public InternalApiKeyMiddleware(RequestDelegate next, IConfiguration configuration, IWebHostEnvironment environment)
    {
        _next = next;
        _apiKey = configuration["AI:InternalApiKey"];
        if (environment.IsProduction() && string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("AI:InternalApiKey must be configured in Production.");
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health") || string.IsNullOrWhiteSpace(_apiKey))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var suppliedKey) ||
            !string.Equals(suppliedKey, _apiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid internal service credential." });
            return;
        }

        await _next(context);
    }
}
