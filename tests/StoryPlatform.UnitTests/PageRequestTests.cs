using StoryPlatform.Application.Common.Models;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class PageRequestTests
{
    [Fact]
    public void PageSize_IsClampedToOneHundred()
    {
        var request = new PageRequest { PageSize = 1_000 };
        Assert.Equal(100, request.PageSize);
    }
}
