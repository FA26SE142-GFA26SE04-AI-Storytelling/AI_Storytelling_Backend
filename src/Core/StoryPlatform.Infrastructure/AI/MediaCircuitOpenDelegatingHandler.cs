using Polly.CircuitBreaker;
using StoryPlatform.Application.Features.MediaGeneration;

namespace StoryPlatform.Infrastructure.AI;

internal sealed class MediaCircuitOpenDelegatingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (BrokenCircuitException exception)
        {
            throw new TransientMediaGenerationException("MEDIA_CIRCUIT_OPEN", exception);
        }
    }
}

