using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ObjectLocatorParsingTests
{
    [Fact]
    public void Parse_ConvertsGeminiCoordinates()
    {
        var result = ObjectLocatorResponse.Parse("{\"found\":true,\"confidence\":0.9,\"box_2d\":[500,400,700,600]}");
        Assert.NotNull(result);
        Assert.Equal(0.4, result!.X, 3);
        Assert.Equal(0.5, result.Y, 3);
        Assert.Equal(0.2, result.W, 3);
        Assert.Equal(0.2, result.H, 3);
    }

    [Theory]
    [InlineData("{\"found\":false}")]
    [InlineData("{\"found\":true,\"confidence\":0.2,\"box_2d\":[500,400,700,600]}")]
    [InlineData("{\"found\":true,\"confidence\":0.9,\"box_2d\":[700,600,500,400]}")]
    [InlineData("{\"found\":true,\"confidence\":0.9,\"box_2d\":[0,0,1000,1000]}")]
    [InlineData("not json")]
    public void Parse_RejectsUnusableBoxes(string json) => Assert.Null(ObjectLocatorResponse.Parse(json));
}
