using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace StoryPlatform.Infrastructure.AI;

public sealed class MediaServicesHealthCheck(
    IOptions<VertexOptions> vertexOptions,
    IOptions<GeminiOptions> geminiOptions) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(5));
        var probeToken = timeoutSource.Token;
        try
        {
            var vertex = vertexOptions.Value;
            var host = vertex.UseVertex
                ? VertexUrlResolver.ResolveHost(vertex)
                : new Uri(geminiOptions.Value.Endpoint).Host;
            var addresses = await Dns.GetHostAddressesAsync(host, probeToken).ConfigureAwait(false);
            if (addresses.Length == 0)
                return HealthCheckResult.Degraded($"DNS không trả địa chỉ cho {host}.");

            using var client = new TcpClient();
            await client.ConnectAsync(host, 443, probeToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy($"Media endpoint {host}:443 reachable.");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded("Media endpoint probe timed out after 5 seconds.", exception);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded("Media endpoint unavailable.", exception);
        }
    }
}
