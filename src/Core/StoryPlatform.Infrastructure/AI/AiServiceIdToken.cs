using System.Net.Http.Headers;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>ID token Google để qua lớp IAM của Cloud Run khi Core gọi AI API; null khi không cấu hình audience.</summary>
public interface IAiServiceIdTokenProvider
{
    Task<string?> GetIdTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Xin ID token cho audience là URL canonical của AI API. <see cref="OidcToken"/> tự cache và làm mới token,
/// nên chỉ tạo một lần. Không log token hay credential.
/// </summary>
public sealed class GoogleAiServiceIdTokenProvider(
    IOptions<AIServiceOptions> options, ILogger<GoogleAiServiceIdTokenProvider> logger) : IAiServiceIdTokenProvider
{
    private readonly AIServiceOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OidcToken? _oidcToken;

    public async Task<string?> GetIdTokenAsync(CancellationToken cancellationToken = default)
    {
        var audience = _options.IdTokenAudience?.Trim();
        if (string.IsNullOrEmpty(audience))
        {
            return null;
        }

        if (_oidcToken == null)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_oidcToken == null)
                {
                    var credential = string.IsNullOrWhiteSpace(_options.GoogleCredentialsJson)
                        ? await GoogleCredential.GetApplicationDefaultAsync(cancellationToken)
                        : GoogleCredential.FromJson(_options.GoogleCredentialsJson);
                    _oidcToken = await credential.GetOidcTokenAsync(
                        OidcTokenOptions.FromTargetAudience(audience), cancellationToken);
                    logger.LogInformation("AI service ID token provider initialised for audience {Audience}.", audience);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        return await _oidcToken.GetAccessTokenAsync(cancellationToken);
    }
}

/// <summary>Gắn <c>Authorization: Bearer &lt;ID token&gt;</c> vào request tới AI API; khoá nội bộ vẫn gửi ở header riêng.</summary>
public sealed class AiServiceIdTokenHandler(IAiServiceIdTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokens.GetIdTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
