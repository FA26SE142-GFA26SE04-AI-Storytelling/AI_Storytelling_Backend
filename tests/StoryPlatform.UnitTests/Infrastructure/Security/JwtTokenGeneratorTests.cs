using StoryPlatform.Infrastructure.Security;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.Security;

public class JwtTokenGeneratorTests
{
    private static JwtTokenGenerator MakeGenerator() =>
        new(JwtAuthenticationConfigurationTests.BuildValidConfiguration());

    [Fact]
    public void GenerateMfaChallengeToken_ThenValidate_ReturnsSameUserId()
    {
        var generator = MakeGenerator();

        var token = generator.GenerateMfaChallengeToken(42);
        var isValid = generator.TryValidateMfaChallengeToken(token, out var userId);

        Assert.True(isValid);
        Assert.Equal(42, userId);
    }

    [Fact]
    public void TryValidateMfaChallengeToken_NormalAccessToken_ReturnsFalse()
    {
        var generator = MakeGenerator();
        var accessToken = generator.GenerateAccessToken(new StoryPlatform.Domain.Entities.UserAccount
        {
            Id = 1, Email = "a@b.com", Username = "a", FullName = "A"
        });

        var isValid = generator.TryValidateMfaChallengeToken(accessToken, out _);

        Assert.False(isValid);
    }

    [Fact]
    public void TryValidateMfaChallengeToken_GarbageString_ReturnsFalse()
    {
        var generator = MakeGenerator();

        var isValid = generator.TryValidateMfaChallengeToken("not-a-jwt", out _);

        Assert.False(isValid);
    }
}
