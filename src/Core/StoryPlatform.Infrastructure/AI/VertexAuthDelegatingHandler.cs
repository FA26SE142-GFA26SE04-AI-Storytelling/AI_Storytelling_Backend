using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Delegating handler that injects a Google OAuth2 Bearer token into every outgoing request.
/// Intended to be placed in an <c>HttpClient</c> pipeline so that all downstream handlers
/// (including retry logic) benefit from a fresh token.
/// </summary>
public sealed class VertexAuthDelegatingHandler : DelegatingHandler
{
    private readonly VertexTokenProvider _tokenProvider;
    private readonly VertexOptions _options;

    public VertexAuthDelegatingHandler(VertexTokenProvider tokenProvider, IOptions<VertexOptions> options)
    {
        _tokenProvider = tokenProvider;
        _options = options.Value;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_options.UseVertex)
        {
            var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
