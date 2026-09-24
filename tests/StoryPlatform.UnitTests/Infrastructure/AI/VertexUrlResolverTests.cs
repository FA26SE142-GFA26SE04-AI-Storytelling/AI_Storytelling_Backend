using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.AI;

public sealed class VertexUrlResolverTests
{
    [Theory]
    [InlineData("global", "https://aiplatform.googleapis.com/")]
    [InlineData("asia-southeast1", "https://asia-southeast1-aiplatform.googleapis.com/")]
    public void Resolve_routes_global_and_regional_locations(string location, string expectedPrefix)
    {
        var url = VertexUrlResolver.Resolve(
            new VertexOptions { UseVertex = true, ProjectId = "project", Location = location },
            new GeminiOptions(),
            "model");

        Assert.StartsWith(expectedPrefix, url, StringComparison.Ordinal);
        Assert.Contains($"/locations/{location}/", url, StringComparison.Ordinal);
    }
}
