using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Abstractions.Security;

public interface IJwtTokenGenerator
{
    string GenerateAccessToken(UserAccount user);
    string GenerateChildAccessToken(int childProfileId);
    string GenerateMfaChallengeToken(int userId);
    bool TryValidateMfaChallengeToken(string token, out int userId);
    string GenerateRefreshToken();
    DateTime GetExpirationDate();
    DateTime GetRefreshTokenExpirationDate();
    long ExpiresInSeconds { get; }
    long ChildTokenExpiresInSeconds { get; }
}
