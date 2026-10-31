using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoryPlatform.Api.Extensions;
using Xunit;

namespace StoryPlatform.UnitTests.Extensions;

public class ForwardedHeadersConfigurationTests
{
    private const string VpcCidr = "10.0.0.0/16";
    private const string CloudflareCidr = "173.245.48.0/20";
    private const string AlbIp = "10.0.1.25";
    private const string CloudflareIp = "173.245.48.7";
    private const string ClientIp = "198.51.100.9";

    [Fact]
    public async Task ProxyChain_AlbThenCloudflare_ResolvesRealClientIp()
    {
        var context = await InvokeAsync(Trusted(), AlbIp, $"{ClientIp}, {CloudflareIp}");

        Assert.Equal(ClientIp, context.Connection.RemoteIpAddress!.ToString());
    }

    [Fact]
    public async Task ProxyChain_SpoofedLeftmostEntry_IsIgnoredBeyondForwardLimit()
    {
        var context = await InvokeAsync(Trusted(), AlbIp, $"203.0.113.66, {ClientIp}, {CloudflareIp}");

        Assert.Equal(ClientIp, context.Connection.RemoteIpAddress!.ToString());
    }

    [Fact]
    public async Task DirectToAlb_BypassingCloudflare_CannotSpoofClientIp()
    {
        const string attackerIp = "203.0.113.50";
        var context = await InvokeAsync(Trusted(), AlbIp, $"198.51.100.200, {attackerIp}");

        Assert.Equal(attackerIp, context.Connection.RemoteIpAddress!.ToString());
    }

    [Fact]
    public async Task UntrustedSource_ForwardedForHeader_IsIgnored()
    {
        const string sourceIp = "203.0.113.50";
        var context = await InvokeAsync(Trusted(), sourceIp, ClientIp);

        Assert.Equal(sourceIp, context.Connection.RemoteIpAddress!.ToString());
    }

    [Fact]
    public async Task NoKnownNetworksConfigured_ForwardedForHeader_IsIgnored()
    {
        var context = await InvokeAsync(Config(), "172.18.0.1", ClientIp);

        Assert.Equal("172.18.0.1", context.Connection.RemoteIpAddress!.ToString());
    }

    [Fact]
    public void KnownNetworksConfigured_ReadsForwardedForAndProtoWithDefaultLimitOfTwo()
    {
        var options = Options(Trusted());

        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
        Assert.Equal(2, options.ForwardLimit);
        Assert.Equal(
            [System.Net.IPNetwork.Parse(VpcCidr), System.Net.IPNetwork.Parse(CloudflareCidr)],
            options.KnownIPNetworks);
        Assert.Empty(options.KnownProxies);
    }

    [Fact]
    public void NoKnownNetworksConfigured_DisablesForwardedHeadersAndTrustsNoProxy()
    {
        var options = Options(Config());

        Assert.Equal(ForwardedHeaders.None, options.ForwardedHeaders);
        Assert.Empty(options.KnownIPNetworks);
        Assert.Empty(options.KnownProxies);
    }

    [Fact]
    public void ResolveKnownNetworks_TrimsBlanksAndDuplicates()
    {
        var networks = ServiceExtensions.ResolveForwardedHeadersKnownNetworks(
            Config(("ForwardedHeaders:KnownNetworks:0", $" {VpcCidr} "),
                   ("ForwardedHeaders:KnownNetworks:1", ""),
                   ("ForwardedHeaders:KnownNetworks:2", VpcCidr)));

        Assert.Equal([System.Net.IPNetwork.Parse(VpcCidr)], networks);
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.0.0.0/33")]
    [InlineData("not-a-network")]
    public void ResolveKnownNetworks_RejectsNonCidrEntries(string network)
    {
        var configuration = Config(("ForwardedHeaders:KnownNetworks:0", network));

        Assert.Throws<InvalidOperationException>(() =>
            ServiceExtensions.ResolveForwardedHeadersKnownNetworks(configuration));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void NonPositiveForwardLimit_Throws(string limit)
    {
        var configuration = Config(("ForwardedHeaders:KnownNetworks:0", VpcCidr), ("ForwardedHeaders:ForwardLimit", limit));

        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddLoadBalancerForwardedHeaders(configuration));
    }

    private static IConfiguration Trusted() => Config(
        ("ForwardedHeaders:KnownNetworks:0", VpcCidr),
        ("ForwardedHeaders:KnownNetworks:1", CloudflareCidr));

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static ForwardedHeadersOptions Options(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLoadBalancerForwardedHeaders(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    }

    private static async Task<HttpContext> InvokeAsync(IConfiguration configuration, string remoteIp, string forwardedFor)
    {
        var options = Microsoft.Extensions.Options.Options.Create(Options(configuration));
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, options);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;

        await middleware.Invoke(context);
        return context;
    }
}
