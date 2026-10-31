using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace StoryPlatform.AI.Api.Middleware;

/// <summary>
/// Authenticates Core → AI calls with a shared internal credential.
/// <para>
/// The credential is mandatory in every environment: a missing key must never silently
/// downgrade the service into an unauthenticated public endpoint. Only the health and
/// swagger paths stay open, because container health checks and API documentation need
/// them before any credential exists.
/// </para>
/// </summary>
public sealed class InternalApiKeyMiddleware
{
    private const string HeaderName = "X-Internal-Api-Key";
    private readonly RequestDelegate _next;
    private readonly ILogger<InternalApiKeyMiddleware> _logger;
    private readonly byte[] _apiKeyHash;
    private readonly bool _credentialConfigured;

    public InternalApiKeyMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<InternalApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        var apiKey = ResolveApiKey(configuration);
        _apiKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        _credentialConfigured = apiKey.Length > 0;

        if (!_credentialConfigured && !environment.IsDevelopment())
        {
            // A production or staging host without the credential must not start serving traffic.
            throw new InvalidOperationException(
                "AI:InternalApiKey must be configured in every non-Development environment.");
        }

        if (!_credentialConfigured)
        {
            _logger.LogWarning(
                "AI:InternalApiKey is not configured. Only health and swagger paths are served; " +
                "all AI generation endpoints return 503 until the credential is set.");
        }
    }

    /// <summary>
    /// Reads the shared credential. Compose injects <c>AI__InternalApiKey</c>, which the
    /// configuration provider maps onto <c>AI:InternalApiKey</c>. A plain <c>AI_INTERNAL_API_KEY</c>
    /// environment variable is also accepted so a direct <c>dotnet run</c> works without the
    /// compose mapping.
    /// </summary>
    private static string ResolveApiKey(IConfiguration configuration)
    {
        var apiKey = configuration["AI:InternalApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return apiKey.Trim();
        }

        return Environment.GetEnvironmentVariable("AI_INTERNAL_API_KEY")?.Trim() ?? string.Empty;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        if (!_credentialConfigured)
        {
            // Fail closed: no credential means no generation traffic, in any environment.
            context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(
                new { error = "AI service credential is not configured." },
                context.RequestAborted);
            return;
        }

        if (!IsAuthorized(context))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsJsonAsync(
                new { error = "Invalid internal service credential." },
                context.RequestAborted);
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Compares the supplied header against the configured credential in constant time so the
    /// response latency does not reveal how many leading characters were correct. Both values
    /// are hashed first so the comparison length is always fixed regardless of key length.
    /// </summary>
    private bool IsAuthorized(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var suppliedKey) ||
            suppliedKey.Count != 1)
        {
            return false;
        }

        var value = suppliedKey[0];
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var suppliedKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return CryptographicOperations.FixedTimeEquals(_apiKeyHash, suppliedKeyHash);
    }
}