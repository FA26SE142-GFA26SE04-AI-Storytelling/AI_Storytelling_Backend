using StoryPlatform.Application.Common.Security;
using Xunit;

namespace StoryPlatform.UnitTests.Common;

public class TokenHasherTests
{
    [Fact]
    public void Hash_SameInput_ReturnsSameHash()
    {
        var hash1 = TokenHasher.Hash("abc123");
        var hash2 = TokenHasher.Hash("abc123");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Hash_DifferentInput_ReturnsDifferentHash()
    {
        var hash1 = TokenHasher.Hash("abc123");
        var hash2 = TokenHasher.Hash("xyz789");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Hash_ReturnsLowercase64CharHexString()
    {
        var hash = TokenHasher.Hash("sample-token");

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }
}
