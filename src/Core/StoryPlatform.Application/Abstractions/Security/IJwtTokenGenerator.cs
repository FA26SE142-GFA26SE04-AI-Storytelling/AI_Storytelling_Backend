using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Abstractions.Security;

public interface IJwtTokenGenerator
{
    string GenerateAccessToken(UserAccount user);
    string GenerateRefreshToken();
    DateTime GetExpirationDate();
    long ExpiresInSeconds { get; }
}
