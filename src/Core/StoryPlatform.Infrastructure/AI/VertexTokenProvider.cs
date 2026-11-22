using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Provides OAuth2 access tokens for Vertex AI from a Google service account.
///
/// Token caching and refresh are left to the Google credential itself
/// (<c>ITokenAccess.GetAccessTokenForRequestAsync</c>), which knows each token's real
/// expiry and refreshes it before it lapses. This class must not cache tokens on top:
/// a fixed lifetime guess would keep serving a token the credential obtained earlier
/// after it expires, and every Vertex call (images, TTS, planning) would fail with 401
/// until the guess ran out.
/// </summary>
public sealed class VertexTokenProvider : IDisposable
{
    private const string Scope = "https://www.googleapis.com/auth/cloud-platform";

    private readonly VertexOptions _options;
    private readonly ILogger<VertexTokenProvider> _logger;
    private readonly object _credentialLock = new();

    private ITokenAccess? _credential;
    private bool _disposed;

    public VertexTokenProvider(IOptions<VertexOptions> options, ILogger<VertexTokenProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Test seam: uses the given token source instead of loading service account credentials.</summary>
    internal VertexTokenProvider(ITokenAccess tokenAccess, ILogger<VertexTokenProvider> logger)
        : this(Options.Create(new VertexOptions()), logger)
    {
        _credential = tokenAccess;
    }

    /// <summary>Returns a currently valid OAuth2 Bearer token.</summary>
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return EnsureCredential().GetAccessTokenForRequestAsync(Scope, cancellationToken: cancellationToken);
    }

    private ITokenAccess EnsureCredential()
    {
        if (_credential is not null) return _credential;
        lock (_credentialLock)
        {
            return _credential ??= LoadCredential();
        }
    }

    private GoogleCredential LoadCredential()
    {
        const string scope = Scope;

        if (!string.IsNullOrWhiteSpace(_options.CredentialsJson))
        {
            // Never log the JSON: it contains the private key.
            var credential = GoogleCredential.FromJson(_options.CredentialsJson).CreateScoped(scope);
            _logger.LogInformation("Loaded Vertex service account credentials from configuration");
            return credential;
        }
        if (!string.IsNullOrWhiteSpace(_options.CredentialsPath) && File.Exists(_options.CredentialsPath))
        {
            using var stream = File.OpenRead(_options.CredentialsPath);
            var credential = GoogleCredential.FromStream(stream).CreateScoped(scope);
            _logger.LogInformation("Loaded Vertex service account credentials from {Path}", _options.CredentialsPath);
            return credential;
        }
        _logger.LogInformation("Using Application Default Credentials for Vertex AI");
        return GoogleCredential.GetApplicationDefault().CreateScoped(scope);
    }

    public void Dispose() => _disposed = true;
}
