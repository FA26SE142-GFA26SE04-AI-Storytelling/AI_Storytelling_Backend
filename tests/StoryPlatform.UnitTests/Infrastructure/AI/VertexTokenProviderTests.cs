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
    public async Task EveryCall_AsksTheCredential_SoARefreshedTokenIsUsedImmediately()
    {
        // Credential của Google tự làm mới token theo hạn thật. Provider không được giữ token cũ
        // theo một thời hạn đoán trước, nếu không mọi lệnh gọi Vertex sẽ bị 401 sau khi token hết hạn.
        var credential = new RotatingTokenAccess();
        using var provider = new VertexTokenProvider(credential, NullLogger<VertexTokenProvider>.Instance);

        var first = await provider.GetAccessTokenAsync();
        credential.Rotate();
        var second = await provider.GetAccessTokenAsync();

        Assert.Equal(("token-1", "token-2", 2), (first, second, credential.Calls));
    }

    private sealed class RotatingTokenAccess : Google.Apis.Auth.OAuth2.ITokenAccess
    {
        private int _version = 1;
        public int Calls { get; private set; }
        public void Rotate() => _version++;

        public Task<string> GetAccessTokenForRequestAsync(string? authUri = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult($"token-{_version}");
        }
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
