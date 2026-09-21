using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Caches and refreshes a short-lived OAuth2 access token from a Google service account.
/// Tokens are refreshed automatically ~5 minutes before expiry so there are no auth gaps.
/// Thread-safe: multiple threads can call GetAccessTokenAsync concurrently; only one
/// refresh runs at a time.
///
/// Uses <c>ITokenAccess.GetAccessTokenForRequestAsync</c> to obtain tokens.
/// </summary>
public sealed class VertexTokenProvider : IDisposable
{
    private readonly VertexOptions _options;
    private readonly ILogger<VertexTokenProvider> _logger;
    private readonly SemaphoreSlim _refreshLock = new(initialCount: 1, maxCount: 1);

    private GoogleCredential? _credential;
    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;
    private bool _disposed;

    public VertexTokenProvider(IOptions<VertexOptions> options, ILogger<VertexTokenProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns a valid OAuth2 Bearer token, refreshing it if it is expired or about to expire.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_cachedToken is not null && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _cachedToken;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the lock.
            if (_cachedToken is not null && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
                return _cachedToken;

            EnsureCredential();
            var token = await ((ITokenAccess)_credential!).GetAccessTokenForRequestAsync(
                "https://www.googleapis.com/auth/cloud-platform",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Tokens are valid for ~1 hour; cache until 5 min before expiry.
            _cachedToken = token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(3500);
            _logger.LogDebug("Vertex AI access token refreshed, expires in ~1 hour");
            return token;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void EnsureCredential()
    {
        if (_credential is not null) return;

        const string scope = "https://www.googleapis.com/auth/cloud-platform";

        if (!string.IsNullOrWhiteSpace(_options.CredentialsPath) &&
            File.Exists(_options.CredentialsPath))
        {
            using var stream = File.OpenRead(_options.CredentialsPath);
            _credential = GoogleCredential.FromStream(stream).CreateScoped(scope);
            _logger.LogInformation("Loaded Vertex service account credentials from {Path}", _options.CredentialsPath);
        }
        else
        {
            _credential = GoogleCredential.GetApplicationDefault().CreateScoped(scope);
            _logger.LogInformation("Using Application Default Credentials for Vertex AI");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshLock.Dispose();
    }
}
