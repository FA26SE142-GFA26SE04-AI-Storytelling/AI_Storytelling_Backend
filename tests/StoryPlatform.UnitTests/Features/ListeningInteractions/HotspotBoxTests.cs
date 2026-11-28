using StoryPlatform.Application.Features.ListeningInteractions.Services;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class HotspotBoxTests
{
    private static readonly HotspotBox Box = new(7, 0.40, 0.50, 0.20, 0.20);

    [Theory]
    [InlineData(0.50, 0.60, true)]    // giữa khung
    [InlineData(0.37, 0.48, true)]    // lệch trong đệm 0,04
    [InlineData(0.30, 0.60, false)]   // xa khung bên trái
    [InlineData(0.50, 0.80, false)]   // xa khung bên dưới
    [InlineData(0.65, 0.60, false)]   // quá đệm bên phải (0,60 + 0,04 = 0,64)
    public void Hits_UsesPaddedBox(double x, double y, bool expected) => Assert.Equal(expected, Box.Hits(x, y));

    [Theory]
    [InlineData(-0.1, 0.6)]
    [InlineData(1.1, 0.6)]
    [InlineData(0.5, -0.01)]
    [InlineData(0.5, 1.01)]
    [InlineData(double.NaN, 0.6)]
    public void Hits_RejectsPointsOutsideTheImage(double x, double y) => Assert.False(Box.Hits(x, y));

    [Fact]
    public void Hits_NearImageEdge_StaysInsideTheImage()
    {
        var corner = new HotspotBox(1, 0.0, 0.0, 0.2, 0.2);
        Assert.True(corner.Hits(0.0, 0.0));
        Assert.True(corner.Hits(0.23, 0.23));
        Assert.False(corner.Hits(0.25, 0.25));
    }

    [Fact] public void TryParse_RoundTrips() => Assert.Equal(Box, HotspotBox.TryParse(Box.ToJson()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"x\":2}")]
    [InlineData("{\"assetId\":1,\"x\":\"a\",\"y\":0.1,\"w\":0.2,\"h\":0.2}")]
    [InlineData("{\"assetId\":0,\"x\":0.1,\"y\":0.1,\"w\":0.2,\"h\":0.2}")]   // asset không hợp lệ
    [InlineData("{\"assetId\":1,\"x\":0.9,\"y\":0.1,\"w\":0.3,\"h\":0.2}")]   // vượt mép phải
    public void TryParse_BadInput_ReturnsNull(string? json) => Assert.Null(HotspotBox.TryParse(json));

    [Theory]
    [InlineData(0.9, 0.5, 0.3, 0.2, false)]     // vượt mép phải
    [InlineData(0.5, 0.9, 0.2, 0.3, false)]     // vượt mép dưới
    [InlineData(0.1, 0.1, 0.01, 0.01, false)]   // quá nhỏ (diện tích < 0,2%)
    [InlineData(-0.1, 0.1, 0.2, 0.2, false)]    // âm
    [InlineData(0.1, 0.1, 0.0, 0.2, false)]     // rộng bằng 0
    [InlineData(0.1, 0.1, 0.2, 0.2, true)]
    [InlineData(0.8, 0.8, 0.2, 0.2, true)]      // sát mép
    public void IsValid_ChecksBounds(double x, double y, double w, double h, bool expected) =>
        Assert.Equal(expected, HotspotBox.IsValid(new HotspotBox(1, x, y, w, h)));

    [Fact] public void IsValid_Null_IsFalse() => Assert.False(HotspotBox.IsValid(null));
}
