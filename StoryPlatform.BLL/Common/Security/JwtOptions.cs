namespace StoryPlatform.BLL.Common.Security;

public class JwtOptions
{
    public const string SectionName = "JwtSettings";

    public string SecretKey { get; set; } = "SuperSecretKeyForStoryPlatformCapstone2026!MustBeAtLeast32BytesLong";
    public string Issuer { get; set; } = "StoryPlatform";
    public string Audience { get; set; } = "StoryPlatformClient";
    public int ExpiryMinutes { get; set; } = 120;
    public int RefreshTokenExpiryDays { get; set; } = 7;
}
