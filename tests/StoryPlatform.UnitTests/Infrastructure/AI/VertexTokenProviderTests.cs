using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.AI;

/// <summary>Trên ECS không có file khoá hay ADC, nên credential Vertex phải nạp được từ cấu hình (secret).</summary>
public sealed class VertexTokenProviderTests
{
    [Fact]
    public async Task CredentialsJson_IsUsedInsteadOfFallingBackToAdc()
    {
        // JSON hỏng phải làm lộ lỗi ngay; nếu provider bỏ qua và dùng ADC thì lỗi cấu hình sẽ bị che mất.
        using var provider = new VertexTokenProvider(
            Options.Create(new VertexOptions { UseVertex = true, ProjectId = "p", CredentialsJson = "{ not json" }),
            NullLogger<VertexTokenProvider>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => provider.GetAccessTokenAsync());
    }

    [Fact]
    public void CredentialsJson_BindsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:Vertex:UseVertex"] = "true",
            ["AI:Vertex:ProjectId"] = "example-project",
            ["AI:Vertex:Location"] = "us-central1",
            ["AI:Vertex:CredentialsJson"] = "{}"
        }).Build();

        var options = configuration.GetSection(VertexOptions.SectionName).Get<VertexOptions>()!;

        Assert.Equal((true, "example-project", "us-central1", "{}"),
            (options.UseVertex, options.ProjectId, options.Location, options.CredentialsJson));
    }
}
