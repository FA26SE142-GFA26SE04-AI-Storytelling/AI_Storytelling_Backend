using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Gắn OAuth token (ADC hoặc service account) và quota project cho Speech-to-Text.
/// Khác VertexAuthDelegatingHandler: luôn gắn token, kể cả khi AI:Vertex:UseVertex = false.
/// </summary>
public sealed class SpeechToTextAuthHandler(VertexTokenProvider tokenProvider, IOptions<SpeechToTextOptions> options)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrWhiteSpace(options.Value.ProjectId))
        {
            request.Headers.TryAddWithoutValidation("x-goog-user-project", options.Value.ProjectId);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
