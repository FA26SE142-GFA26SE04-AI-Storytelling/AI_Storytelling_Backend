using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoryPlatform.Api.Extensions;
using Xunit;

namespace StoryPlatform.UnitTests.Extensions;

public class ForwardedHeadersConfigurationTests
{
    [Fact]
    public async Task AddLoadBalancerForwardedHeaders_ReplacesRemoteIpWithClientIpFromAlbHeader()
    {
        var services = new ServiceCollection();
        services.AddLoadBalancerForwardedHeaders();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>();
        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask, NullLoggerFactory.Instance, options);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.1.25"); // IP nội bộ của ALB
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.9";

        await middleware.Invoke(context);

        Assert.Equal("198.51.100.9", context.Connection.RemoteIpAddress!.ToString());
    }

    [Fact]
    public void AddLoadBalancerForwardedHeaders_ReadsForwardedForAndProto()
    {
        var services = new ServiceCollection();
        services.AddLoadBalancerForwardedHeaders();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
    }
}
